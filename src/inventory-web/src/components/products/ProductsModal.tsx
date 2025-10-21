import React from 'react';

type Item = { id: string; sku: string; name: string; ean?: string | null; description?: string | null; codeDigits?: string | null };
type Response = {
  items: Item[]; page: number; pageSize: number; total: number; totalPages: number;
  sortBy: string; sortDir: 'asc'|'desc'; q?: string | null;
};

type Props = { open: boolean; onClose: () => void; shopId: string };

export function ProductsModal({ open, onClose, shopId }: Props) {
  const [q, setQ] = React.useState('');
  const [page, setPage] = React.useState(1);
  const [sortBy, setSortBy] = React.useState<'sku'|'ean'|'name'|'descr'|'digits'>('sku');
  const [sortDir, setSortDir] = React.useState<'asc'|'desc'>('asc');
  const [data, setData] = React.useState<Response | null>(null);
  const [loading, setLoading] = React.useState(false);

  React.useEffect(() => {
    if (!open) return;
    let abort = false;
    setLoading(true);
    const params = new URLSearchParams({
      page: String(page),
      pageSize: String(50),
      q: q.trim(),
      sortBy,
      sortDir
    });
    (async () => {
      try {
        const res = await fetch(`/api/shops/${shopId}/products?` + params.toString());
        if (!res.ok) throw new Error('fetch failed');
        const json = await res.json();
        if (!abort) setData(json);
      } finally {
        if (!abort) setLoading(false);
      }
    })();
    return () => { abort = true; };
  }, [open, shopId, page, sortBy, sortDir, q]);

  const setSort = (col: typeof sortBy) => {
    if (sortBy === col) setSortDir(d => (d === 'asc' ? 'desc' : 'asc'));
    else { setSortBy(col); setSortDir('asc'); }
  };

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 bg-black/40" role="dialog" aria-modal="true">
      <div className="absolute inset-x-0 bottom-0 top-8 mx-auto max-w-4xl rounded-t-xl bg-white shadow-lg">
        <div className="sticky top-0 flex items-center justify-between border-b bg-white p-3">
          <input
            placeholder="Rechercher (SKU / EAN / description)"
            className="w-full max-w-sm rounded border border-gray-300 px-3 py-2 text-sm text-gray-700 placeholder:text-gray-400 focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-100"
            value={q}
            onChange={e => { setPage(1); setQ(e.target.value); }}
          />
          <button
            onClick={onClose}
            className="ml-3 rounded border border-gray-300 px-3 py-2 text-sm text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-100"
          >
            Fermer
          </button>
        </div>

        <div className="max-h-[70vh] overflow-y-auto overflow-x-hidden">
          <table className="min-w-full table-fixed border-collapse">
            <thead className="sticky top-0 bg-white">
              <tr className="text-left text-sm text-gray-600">
                <th className="cursor-pointer p-2 font-medium text-gray-900" onClick={() => setSort('sku')}>SKU</th>
                <th className="cursor-pointer p-2 font-medium text-gray-900" onClick={() => setSort('ean')}>EAN</th>
                <th className="cursor-pointer p-2 font-medium text-gray-900" onClick={() => setSort('name')}>Nom</th>
                <th className="cursor-pointer p-2 font-medium text-gray-900" onClick={() => setSort('descr')}>Description</th>
                <th className="cursor-pointer p-2 font-medium text-gray-900" onClick={() => setSort('digits')}>Digits</th>
              </tr>
            </thead>
            <tbody>
              {loading && (!data || data.items.length === 0) ? (
                <tr><td className="p-4 text-sm text-gray-500" colSpan={5}>Chargement…</td></tr>
              ) : data && data.items.length > 0 ? (
                data.items.map((p) => (
                  <tr key={p.id} className="border-t text-sm text-gray-700 hover:bg-gray-50">
                    <td className="p-2 truncate" title={p.sku}>{p.sku}</td>
                    <td className="p-2 truncate" title={p.ean ?? undefined}>{p.ean ?? ''}</td>
                    <td className="p-2 truncate" title={p.name}>{p.name}</td>
                    <td className="p-2 truncate" title={p.description ?? undefined}>{p.description ?? ''}</td>
                    <td className="p-2 truncate" title={p.codeDigits ?? undefined}>{p.codeDigits ?? ''}</td>
                  </tr>
                ))
              ) : (
                <tr><td className="p-4 text-sm text-gray-500" colSpan={5}>Aucun résultat</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <div className="flex items-center justify-between border-t p-3 text-sm text-gray-600">
          <div>
            {data ? <>Page {data.page} / {data.totalPages} — {data.total} éléments</> : '—'}
          </div>
          <div className="flex gap-2">
            <button
              disabled={!data || data.page <= 1}
              onClick={() => setPage(p => Math.max(1, p - 1))}
              className="rounded border border-gray-300 px-3 py-1 text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-100 disabled:cursor-not-allowed disabled:opacity-50"
            >
              Préc.
            </button>
            <button
              disabled={!data || (data.totalPages === 0) || (data.page >= data.totalPages)}
              onClick={() => setPage(p => p + 1)}
              className="rounded border border-gray-300 px-3 py-1 text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-100 disabled:cursor-not-allowed disabled:opacity-50"
            >
              Suiv.
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
