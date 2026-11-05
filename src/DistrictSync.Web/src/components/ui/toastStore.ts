import { createStore } from '@/lib/store';

export type ToastTone = 'info' | 'success' | 'warning' | 'error';

export interface Toast {
  id: number;
  tone: ToastTone;
  title: string;
  description?: string;
  /** Optional in-app link (react-router path). */
  href?: string;
  createdAt: number;
  durationMs: number;
}

export const toastStore = createStore<Toast[]>([]);
let nextId = 1;

interface ToastOptions {
  description?: string;
  href?: string;
  durationMs?: number;
}

function push(tone: ToastTone, title: string, options: ToastOptions = {}): number {
  const id = nextId++;
  const toast: Toast = {
    id,
    tone,
    title,
    description: options.description,
    href: options.href,
    createdAt: Date.now(),
    durationMs: options.durationMs ?? (tone === 'error' ? 9000 : 5000),
  };
  toastStore.set((list) => [...list.slice(-5), toast]);
  window.setTimeout(() => dismissToast(id), toast.durationMs);
  return id;
}

export function dismissToast(id: number): void {
  toastStore.set((list) => (list.some((t) => t.id === id) ? list.filter((t) => t.id !== id) : list));
}

export const toast = {
  info: (title: string, options?: ToastOptions) => push('info', title, options),
  success: (title: string, options?: ToastOptions) => push('success', title, options),
  warning: (title: string, options?: ToastOptions) => push('warning', title, options),
  error: (title: string, options?: ToastOptions) => push('error', title, options),
};
