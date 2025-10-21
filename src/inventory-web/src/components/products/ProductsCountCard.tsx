import React from 'react';

type Props = {
  shopId: string;
  onClick: () => void;
};

export function ProductsCountCard({ shopId, onClick }: Props) {
  const [state, setState] = React.useState<{ count: number; hasCatalog: boolean } | null>(null);
  const [loading, setLoading] = React.useState(true);

  React.useEffect(() => {
    let aborted = false;
    (async () => {
      try {
        const res = await fetch(`/api/shops/${shopId}/products/count`);
        if (!res.ok) throw new Error('count failed');
        const json = await res.json();
        if (!aborted) setState(json);
      } finally {
        if (!aborted) setLoading(false);
      }
    })();
    return () => { aborted = true; };
  }, [shopId]);

  return (
    <button
      type="button"
      onClick={onClick}
      className="w-full rounded-lg border p-4 shadow-sm text-left focus:outline-none focus:ring"
      aria-label="Ouvrir le catalogue produits"
    >
      {loading ? (
        <div className="h-5 w-24 animate-pulse rounded bg-gray-200" />
      ) : state?.count === 0 ? (
        <p className="text-sm text-gray-500">Aucun produit en base</p>
      ) : (
        <div className="flex items-baseline gap-2">
          <span className="text-lg font-semibold">{state?.count ?? '—'}</span>
          <span className="text-sm text-gray-600">produits</span>
        </div>
      )}
    </button>
  );
}
