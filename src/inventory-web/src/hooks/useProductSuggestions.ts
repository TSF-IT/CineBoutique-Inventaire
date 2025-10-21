import React from 'react';

export function useProductSuggestions() {
  const [items, setItems] = React.useState<Array<{ sku: string; ean?: string | null; name: string }>>([]);
  const [loading, setLoading] = React.useState(false);
  const controller = React.useRef<AbortController | null>(null);

  const query = React.useCallback(async (q: string, limit = 8) => {
    if (!q || q.trim().length === 0) { setItems([]); return; }
    controller.current?.abort();
    controller.current = new AbortController();
    setLoading(true);
    try {
      const res = await fetch(`/api/products/suggest?q=${encodeURIComponent(q)}&limit=${limit}`, { signal: controller.current.signal });
      if (!res.ok) throw new Error();
      const json = await res.json();
      setItems(json as Array<{ sku: string; ean?: string | null; name: string }>);
    } catch {
      /* noop */
    } finally {
      setLoading(false);
    }
  }, []);

  return { items, loading, query };
}
