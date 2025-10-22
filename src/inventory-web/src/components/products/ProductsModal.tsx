import React from 'react';
import { FixedSizeList as VirtualList } from 'react-window';

const ROW_HEIGHT = 44;
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
  const listRef = React.useRef<{ scrollToItem?: (index: number) => void } | null>(null);

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

  const setSort = React.useCallback((col: typeof sortBy) => {
    if (sortBy === col) setSortDir(d => (d === 'asc' ? 'desc' : 'asc'));
    else { setSortBy(col); setSortDir('asc'); }
  }, [sortBy]);

  React.useEffect(() => {
    listRef.current?.scrollToItem?.(0);
  }, [page, sortBy, sortDir, q, data?.items]);

  const header = React.useMemo(() => (
    <thead className="sticky top-0 bg-white">
      <tr className="text-left text-sm text-gray-600">
        {([['sku', 'SKU'], ['ean', 'EAN'], ['name', 'Nom'], ['descr', 'Description'], ['digits', 'Digits']] as const).map(([key, label]) => (
          <th
            key={key}
            className="cursor-pointer p-2 font-medium text-gray-900"
            onClick={() => setSort(key)}
          >
            <span className="inline-flex items-center gap-1">
              {label}
              {sortBy === key ? (
                <span aria-hidden="true">{sortDir === 'asc' ? '↑' : '↓'}</span>
              ) : null}
            </span>
          </th>
        ))}
      </tr>
    </thead>
  ), [setSort, sortBy, sortDir]);

  if (!open) return null;

  const items = data?.items ?? [];
  const canVirtualize = (data?.items?.length ?? 0) >= 200;
  const itemKey = React.useCallback((index: number, products: Item[]) => products[index]?.id ?? index, []);

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

        <div className="max-h-[70vh] overflow-x-hidden">
          {loading && (!data || items.length === 0) ? (
            <table className="min-w-full table-fixed border-collapse">
              {header}
              <tbody>
                <tr><td className="p-4 text-sm text-gray-500" colSpan={5}>Chargement…</td></tr>
              </tbody>
            </table>
          ) : items.length === 0 ? (
            <table className="min-w-full table-fixed border-collapse">
              {header}
              <tbody>
                <tr><td className="p-4 text-sm text-gray-500" colSpan={5}>Aucun résultat</td></tr>
              </tbody>
            </table>
          ) : !canVirtualize ? (
            <table className="min-w-full table-fixed border-collapse">
              {header}
              <tbody>
                {items.map((product, index) => (
                  <tr
                    key={itemKey(index, items)}
                    className="border-t text-sm text-gray-700 hover:bg-gray-50"
                  >
                    <td className="p-2 truncate" title={product.sku}>{product.sku}</td>
                    <td className="p-2 truncate" title={product.ean ?? undefined}>{product.ean ?? ''}</td>
                    <td className="p-2 truncate" title={product.name}>{product.name}</td>
                    <td className="p-2 truncate" title={product.description ?? undefined}>{product.description ?? ''}</td>
                    <td className="p-2 truncate" title={product.codeDigits ?? undefined}>{product.codeDigits ?? ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <VirtualList
              ref={listRef as React.Ref<any>}
              height={Math.min(440, Math.max(220, (data?.items?.length ?? 0) * ROW_HEIGHT))}
              itemCount={data!.items.length}
              itemSize={ROW_HEIGHT}
              width="100%"
              className="border-t"
            >
              {({ index, style }: { index: number; style: React.CSSProperties }) => {
                const p = data!.items[index];
                return (
                  <div style={style} className="text-sm grid grid-cols-5">
                    <div className="p-2">{p.sku}</div>
                    <div className="p-2">{p.ean ?? ''}</div>
                    <div className="p-2">{p.name}</div>
                    <div className="p-2">{p.description ?? ''}</div>
                    <div className="p-2">{p.codeDigits ?? ''}</div>
                  </div>
                );
              }}
            </VirtualList>
          )}
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
