import { useEffect, useMemo, useRef, useState } from "react";

export interface SuggestItem {
  sku: string;
  ean?: string | null;
  name: string;
  group?: string | null;
  subGroup?: string | null;
}

export function useProductSuggest(q: string, limit = 8, debounceMs = 150) {
  const [data, setData] = useState<SuggestItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const timer = useRef<number | undefined>(undefined);
  const ctrl = useRef<AbortController | null>(null);

  const url = useMemo(() => {
    const query = (q ?? "").trim();
    if (!query) return null;
    const p = new URLSearchParams({ q: query, limit: String(limit) });
    return `/api/products/suggest?${p.toString()}`;
  }, [q, limit]);

  useEffect(() => {
    if (!url) { setData([]); setError(null); return; }
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(async () => {
      ctrl.current?.abort();
      ctrl.current = new AbortController();
      setLoading(true); setError(null);
      try {
        const r = await fetch(url, { signal: ctrl.current.signal });
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        const js = await r.json();
        setData(Array.isArray(js) ? js : []);
      } catch (rawError) {
        const isAbort =
          rawError instanceof DOMException && rawError.name === "AbortError";
        if (!isAbort) {
          const error = rawError instanceof Error ? rawError : new Error(String(rawError));
          setError(error);
          setData([]);
        }
      } finally {
        setLoading(false);
      }
    }, debounceMs);
    return () => {
      window.clearTimeout(timer.current);
      ctrl.current?.abort();
    };
  }, [url, debounceMs]);

  return { data, loading, error };
}
