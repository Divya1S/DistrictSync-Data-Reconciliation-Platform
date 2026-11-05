import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';

type Primitive = string | number | boolean;

/**
 * Table filters that live in the URL (?status=Failed&page=2) so views are shareable and survive reloads.
 * `defaults` must be a stable (module-level) object. Changing any non-page filter resets `page`.
 */
export function useSearchFilters<T extends Record<string, Primitive>>(defaults: T): [T, (patch: Partial<T>) => void] {
  const [params, setParams] = useSearchParams();

  const filters = useMemo(() => {
    const out: Record<string, Primitive> = { ...defaults };
    for (const key of Object.keys(defaults)) {
      const raw = params.get(key);
      if (raw === null) continue;
      const def = defaults[key];
      if (typeof def === 'number') {
        const n = Number(raw);
        if (!Number.isNaN(n)) out[key] = n;
      } else if (typeof def === 'boolean') {
        out[key] = raw === 'true' || raw === '1';
      } else {
        out[key] = raw;
      }
    }
    return out as T;
  }, [params, defaults]);

  const setFilters = useCallback(
    (patch: Partial<T>) => {
      void setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          const touchesNonPage = Object.keys(patch).some((k) => k !== 'page');
          if (touchesNonPage && 'page' in defaults) next.delete('page');
          for (const [k, v] of Object.entries(patch)) {
            if (v === undefined || v === defaults[k] || v === '' || v === false) next.delete(k);
            else next.set(k, String(v));
          }
          return next;
        },
        { replace: true },
      );
    },
    [setParams, defaults],
  );

  return [filters, setFilters];
}
