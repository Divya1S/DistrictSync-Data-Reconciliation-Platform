import type { ProblemDetails } from './types';

/** Origin override for the API (VITE_API_BASE); empty string means same origin (Vite dev proxy / reverse proxy). */
export const API_ORIGIN = (import.meta.env.VITE_API_BASE ?? '').replace(/\/+$/, '');
export const API_BASE = `${API_ORIGIN}/api/v1`;
export const HUB_URL = `${API_ORIGIN}/hubs/operations`;

export type QueryValue = string | number | boolean | null | undefined;
export type QueryParams = Record<string, QueryValue>;

/** Builds a query string, skipping null/undefined/empty values. */
export function qs(params?: QueryParams): string {
  if (!params) return '';
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue;
    search.set(key, String(value));
  }
  const s = search.toString();
  return s ? `?${s}` : '';
}

/** RFC 7807 problem details surfaced as a typed error. */
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly detail: string | undefined;
  readonly type: string | undefined;
  readonly errors: Record<string, string[]> | undefined;
  readonly problem: ProblemDetails | undefined;

  constructor(status: number, problem?: ProblemDetails, fallbackMessage?: string) {
    const title = problem?.title ?? fallbackMessage ?? `Request failed (${status})`;
    super(problem?.detail ?? title);
    this.name = 'ApiError';
    this.status = status;
    this.title = title;
    this.detail = problem?.detail;
    this.type = problem?.type;
    this.errors = problem?.errors;
    this.problem = problem;
  }

  /** A single flat list of validation messages ("field: message"). */
  get validationMessages(): string[] {
    if (!this.errors) return [];
    return Object.entries(this.errors).flatMap(([field, messages]) =>
      messages.map((m) => (field ? `${field}: ${m}` : m)),
    );
  }

  get isUnauthorized(): boolean {
    return this.status === 401;
  }

  get isPreconditionFailed(): boolean {
    return this.status === 412;
  }
}

export function isApiError(e: unknown): e is ApiError {
  return e instanceof ApiError;
}

/** Human-friendly message for any thrown value. */
export function errorMessage(e: unknown): string {
  if (isApiError(e)) {
    const v = e.validationMessages;
    if (v.length > 0) return v.join('; ');
    return e.detail ?? e.title;
  }
  if (e instanceof Error) return e.message;
  return 'Something went wrong';
}

interface ClientConfig {
  getToken: () => string | null;
  onUnauthorized: () => void;
}

let config: ClientConfig = {
  getToken: () => null,
  onUnauthorized: () => undefined,
};

export function configureClient(next: ClientConfig): void {
  config = next;
}

export interface RequestOptions {
  params?: QueryParams;
  headers?: Record<string, string>;
  signal?: AbortSignal;
  /** Skip the 401 → logout behaviour (used by the dev-token endpoint). */
  anonymous?: boolean;
}

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

async function parseProblem(response: Response): Promise<ProblemDetails | undefined> {
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('json')) {
    const text = await response.text().catch(() => '');
    return text ? { title: text.slice(0, 300) } : undefined;
  }
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return undefined;
  }
}

async function send<T>(method: Method, path: string, body: BodyInit | undefined, options: RequestOptions = {}, isJsonBody = false): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json', ...options.headers };
  if (isJsonBody) headers['Content-Type'] = 'application/json';
  const token = options.anonymous ? null : config.getToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  let response: Response;
  try {
    response = await fetch(`${API_BASE}${path}${qs(options.params)}`, { method, headers, body, signal: options.signal });
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') throw e;
    throw new ApiError(0, { title: 'Network error', detail: 'The API could not be reached. Check that the backend is running.' });
  }

  if (!response.ok) {
    if (response.status === 401 && !options.anonymous) config.onUnauthorized();
    const problem = await parseProblem(response);
    throw new ApiError(response.status, problem, response.statusText || undefined);
  }

  if (response.status === 204 || response.headers.get('content-length') === '0') {
    return undefined as T;
  }
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('json')) {
    return (await response.text()) as unknown as T;
  }
  return (await response.json()) as T;
}

export const api = {
  get: <T>(path: string, options?: RequestOptions) => send<T>('GET', path, undefined, options),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    send<T>('POST', path, body === undefined ? undefined : JSON.stringify(body), options, body !== undefined),
  put: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    send<T>('PUT', path, body === undefined ? undefined : JSON.stringify(body), options, body !== undefined),
  delete: <T>(path: string, options?: RequestOptions) => send<T>('DELETE', path, undefined, options),
  /** multipart/form-data upload; the browser sets the boundary header. */
  postForm: <T>(path: string, form: FormData, options?: RequestOptions) => send<T>('POST', path, form, options, false),
};
