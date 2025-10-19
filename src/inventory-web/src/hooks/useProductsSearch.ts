import { useEffect, useMemo, useRef, useState } from "react";

export interface ProductRow {
  sku: string;
  ean?: string | null;
  name: string;
  group?: string | null;
  subGroup?: string | null;
}

export function useProductsSearch(q: string, debounceMs = 200) {
  const [rows, setRows] = useState<ProductRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const timer = useRef<number | undefined>(undefined);
  const ctrl = useRef<AbortController | null>(null);

  const url = useMemo(() => {
    const query = (q ?? "").trim();
    // L’endpoint /api/products/search existe dans l’API/backend
    // (on garde la sémantique minimale q=?)
    const p = new URLSearchParams({ q: query });
    return `/api/products/search?${p.toString()}`;
  }, [q]);

  useEffect(() => {
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(async () => {
      ctrl.current?.abort();
      ctrl.current = new AbortController();
      setLoading(true); setError(null);
      try {
        const r = await fetch(url, { signal: ctrl.current.signal });
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        const js = await r.json();
        // On s’aligne sur la forme renvoyée : si c’est un tableau, on le prend tel quel.
        setRows(Array.isArray(js) ? js : (js?.items ?? []));
      } catch (e: any) {
        if (e?.name !== "AbortError") { setError(e); setRows([]); }
      } finally {
        setLoading(false);
      }
    }, debounceMs);
    return () => {
      window.clearTimeout(timer.current);
      ctrl.current?.abort();
    };
  }, [url, debounceMs]);

  return { rows, loading, error };
}
