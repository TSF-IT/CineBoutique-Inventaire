import { useEffect, useState } from "react";

export function useProductsCount(pollMs = 0) {
  const [total, setTotal] = useState<number | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  async function fetchOnce() {
    setLoading(true); setError(null);
    try {
      const r = await fetch("/api/products/count");
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      const js = await r.json();
      setTotal(typeof js?.total === "number" ? js.total : null);
    } catch (rawError) {
      const error = rawError instanceof Error ? rawError : new Error(String(rawError));
      setError(error);
      setTotal(null);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    fetchOnce();
    if (pollMs > 0) {
      const id = setInterval(fetchOnce, pollMs);
      return () => clearInterval(id);
    }
  }, [pollMs]);

  return { total, loading, error, refresh: fetchOnce };
}
