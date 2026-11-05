/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Optional absolute origin of the API; empty means "same origin / dev proxy". */
  readonly VITE_API_BASE?: string;
}
