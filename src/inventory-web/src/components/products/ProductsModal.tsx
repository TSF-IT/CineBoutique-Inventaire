import React from "react";
import { FixedSizeList as VirtualList } from "react-window";

import { modalOverlayClassName } from "@/app/components/Modal/modalOverlayClassName";

const GRID_TEMPLATE_CLASS =
  "grid grid-cols-[minmax(140px,1fr)_minmax(120px,1fr)_minmax(260px,1.6fr)] sm:grid-cols-[minmax(160px,1fr)_minmax(140px,1fr)_minmax(360px,2fr)]";

const ROW_HEIGHT = 44;
type Item = {
  id: string;
  sku: string;
  name: string;
  ean?: string | null;
  codeDigits?: string | null;
};
type Response = {
  items: Item[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
  sortBy: string;
  sortDir: "asc" | "desc";
  q?: string | null;
};

type Props = {
  open: boolean
  onClose: () => void
  shopId: string
  onSelect?: (product: Item) => Promise<boolean> | boolean
  selectLabel?: string
};

const columns = [
  { key: "ean", label: "EAN/RFID" },
  { key: "sku", label: "SKU/item" },
  { key: "name", label: "Description" },
] as const;

type ColumnKey = (typeof columns)[number]["key"];

const VirtualizedRowGroup = React.forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  function VirtualizedRowGroup(props, ref) {
    return <div {...props} ref={ref} role="rowgroup" />;
  }
);

export type ProductsModalItem = Item

export function ProductsModal({ open, onClose, shopId, onSelect, selectLabel }: Props) {
  const [q, setQ] = React.useState("");
  const [page, setPage] = React.useState(1);
  const [sortBy, setSortBy] = React.useState<ColumnKey>("sku");
  const [sortDir, setSortDir] = React.useState<"asc" | "desc">("asc");
  const [data, setData] = React.useState<Response | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [pendingSelectionId, setPendingSelectionId] = React.useState<string | null>(null);
  const listRef = React.useRef<{
    scrollToItem?: (index: number) => void;
  } | null>(null);
  const containerRef = React.useRef<HTMLDivElement | null>(null);

  const isInteractive = typeof onSelect === "function";
  const resolvedSelectLabel = selectLabel ?? "Ajouter";

  React.useEffect(() => {
    if (!open) return;
    let abort = false;
    setLoading(true);
    const params = new URLSearchParams({
      page: String(page),
      pageSize: String(50),
      q: q.trim(),
      sortBy,
      sortDir,
    });
    (async () => {
      try {
        const res = await fetch(
          `/api/shops/${shopId}/products?` + params.toString()
        );
        if (!res.ok) throw new Error("fetch failed");
        const json = await res.json();
        if (!abort) setData(json);
      } finally {
        if (!abort) setLoading(false);
      }
    })();
    return () => {
      abort = true;
    };
  }, [open, shopId, page, sortBy, sortDir, q]);

  const setSort = React.useCallback(
    (col: typeof sortBy) => {
      if (sortBy === col) setSortDir((d) => (d === "asc" ? "desc" : "asc"));
      else {
        setSortBy(col);
        setSortDir("asc");
      }
    },
    [sortBy]
  );

  React.useEffect(() => {
    listRef.current?.scrollToItem?.(0);
  }, [page, sortBy, sortDir, q, data?.items]);

  React.useEffect(() => {
    if (!open) {
      setPendingSelectionId(null);
      return;
    }

    const previouslyFocused = document.activeElement as HTMLElement | null;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
      }
    };

    requestAnimationFrame(() => {
      containerRef.current?.focus?.();
    });

    document.addEventListener("keydown", handleKeyDown);

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      previouslyFocused?.focus?.();
    };
  }, [open, onClose]);

  const handleOverlayClick = React.useCallback(
    (event: React.MouseEvent<HTMLDivElement>) => {
      if (event.target === event.currentTarget) {
        onClose();
      }
    },
    [onClose]
  );

  const handleClearSearch = React.useCallback(() => {
    setPage(1);
    setQ("");
  }, []);

  const handleRowActivation = React.useCallback(
    async (product: Item) => {
      if (!onSelect || pendingSelectionId) {
        return;
      }
      setPendingSelectionId(product.id);
      try {
        const result = await onSelect(product);
        if (result) {
          onClose();
        }
      } finally {
        setPendingSelectionId(null);
      }
    },
    [onClose, onSelect, pendingSelectionId]
  );

  const header = React.useMemo(
    () => (
      <div
        role="row"
        className={`${GRID_TEMPLATE_CLASS} items-center gap-3 border-b border-slate-200/80 bg-white/95 px-4 py-3 text-[0.75rem] font-semibold uppercase tracking-wide text-slate-500 backdrop-blur dark:border-slate-800 dark:bg-slate-900/90 dark:text-slate-300`}
      >
        {columns.map(({ key, label }) => {
          const isActive = sortBy === key;
          const ariaSort: React.AriaAttributes["aria-sort"] = isActive
            ? sortDir === "asc"
              ? "ascending"
              : "descending"
            : "none";

          return (
            <div
              key={key}
              role="columnheader"
              aria-sort={ariaSort}
              className="group flex cursor-pointer select-none items-center gap-2 text-left text-slate-600 transition hover:text-product-700 focus:outline-none focus-visible:text-product-700 dark:text-slate-200 dark:hover:text-product-200"
              tabIndex={0}
              onClick={() => setSort(key)}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  setSort(key);
                }
              }}
            >
              <span>{label}</span>
              <span className="sr-only">
                {isActive
                  ? sortDir === "asc"
                    ? "Tri croissant"
                    : "Tri décroissant"
                  : "Activer le tri"}
              </span>
              <span
                aria-hidden="true"
                className={`w-3 text-xs font-bold leading-none transition ${
                  isActive
                    ? "opacity-100 text-product-600 dark:text-product-300"
                    : "opacity-0 text-slate-400"
                }`}
              >
                {sortDir === "asc" ? "↑" : "↓"}
              </span>
            </div>
          );
        })}
      </div>
    ),
    [setSort, sortBy, sortDir]
  );

  const items = data?.items ?? [];
  const itemCount = items.length;
  const canVirtualize = itemCount >= 200;
  const itemKey = React.useCallback(
    (index: number, products: Item[]) => products[index]?.id ?? index,
    []
  );

  const commonRowClassName = `${GRID_TEMPLATE_CLASS} relative items-center gap-3 border-b border-slate-200/70 bg-white/80 px-4 py-2.5 text-left text-sm transition dark:border-slate-800 dark:bg-slate-900/40`;

  if (!open) return null;

  return (
    <div className={modalOverlayClassName} role="presentation" onClick={handleOverlayClick}>
      <div
        ref={containerRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="products-modal-title"
        className="relative flex w-full max-w-4xl flex-col overflow-hidden rounded-3xl bg-white shadow-2xl outline-none focus-visible:ring-2 focus-visible:ring-product-600 dark:bg-slate-900"
        tabIndex={-1}
      >
        <header className="flex flex-col gap-4 border-b border-product-200 bg-product-50/80 px-5 py-5 dark:border-product-700/60 dark:bg-product-700/30 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex-1">
            <p className="text-xs font-semibold uppercase tracking-wide text-product-700 dark:text-product-200">
              Catalogue produits
            </p>
            <h2 id="products-modal-title" className="mt-1 text-xl font-semibold text-slate-900 dark:text-white">
              Produits disponibles
            </h2>
            <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
              Recherchez une référence par SKU, EAN ou nom pour vérifier votre inventaire.
            </p>
            <div className="mt-4">
              <label htmlFor="products-search" className="sr-only">
                Rechercher un produit
              </label>
              <div className="relative flex items-center">
                <span className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-400 dark:text-slate-500" aria-hidden="true">
                  <svg
                    xmlns="http://www.w3.org/2000/svg"
                    viewBox="0 0 20 20"
                    fill="currentColor"
                    className="h-5 w-5"
                  >
                    <path
                      fillRule="evenodd"
                      d="M9 3.5a5.5 5.5 0 1 0 3.356 9.9l3.622 3.621a.75.75 0 1 0 1.06-1.06l-3.62-3.623A5.5 5.5 0 0 0 9 3.5Zm-4 5.5a4 4 0 1 1 8 0 4 4 0 0 1-8 0Z"
                      clipRule="evenodd"
                    />
                  </svg>
                </span>
                <input
                  id="products-search"
                  placeholder="Rechercher (SKU / EAN / nom)"
                  className="w-full rounded-full border border-product-200 bg-white px-10 py-2.5 text-sm text-slate-900 shadow-inner placeholder:text-slate-400 focus:border-product-600 focus:outline-none focus:ring-2 focus:ring-product-200 dark:border-slate-700 dark:bg-slate-800 dark:text-white dark:placeholder:text-slate-400 dark:focus:border-product-600 dark:focus:ring-product-600/30"
                  value={q}
                  onChange={(e) => {
                    setPage(1);
                    setQ(e.target.value);
                  }}
                />
                {q ? (
                  <button
                    type="button"
                    onClick={handleClearSearch}
                    className="absolute inset-y-0 right-2 inline-flex items-center justify-center rounded-full p-2 text-slate-400 transition hover:text-product-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-product-600 dark:text-slate-300 dark:hover:text-product-200"
                  >
                    <span className="sr-only">Effacer la recherche</span>
                    <span aria-hidden="true">✕</span>
                  </button>
                ) : null}
              </div>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="inline-flex shrink-0 items-center justify-center rounded-full border border-product-200 p-3 text-product-700 transition hover:bg-product-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-product-600 focus-visible:ring-offset-2 dark:border-product-700 dark:text-product-200 dark:hover:bg-product-700/40"
            aria-label="Fermer"
          >
            <span aria-hidden="true">✕</span>
          </button>
        </header>

        <div className="flex flex-1 flex-col overflow-hidden bg-white dark:bg-slate-900">
          <div className="flex-1 overflow-hidden px-5 py-5">
            <div className="flex h-full flex-col gap-4">
              <div className="overflow-hidden rounded-3xl border border-slate-200 bg-white/95 shadow-elev-1 dark:border-slate-700 dark:bg-slate-900/40">
                <div className="max-h-[60vh] overflow-y-auto">
                  <div
                    role="table"
                    aria-label="Liste des produits"
                    className="min-w-full text-sm text-slate-700 dark:text-slate-200"
                  >
                    <div
                      role="rowgroup"
                      className="sticky top-0 z-20 bg-white/95 backdrop-blur dark:bg-slate-900/90"
                    >
                      {header}
                    </div>
                    <div role="rowgroup">
                      {loading && (!data || items.length === 0) ? (
                        <div
                          role="row"
                          className={`${GRID_TEMPLATE_CLASS} items-center gap-3 px-4 py-5 text-sm text-slate-500 dark:text-slate-300`}
                        >
                          <div role="cell" className="col-span-full text-center">
                            Chargement…
                          </div>
                        </div>
                      ) : items.length === 0 ? (
                        <div
                          role="row"
                          className={`${GRID_TEMPLATE_CLASS} items-center gap-3 px-4 py-5 text-sm text-slate-500 dark:text-slate-300`}
                        >
                          <div role="cell" className="col-span-full text-center">
                            Aucun résultat
                          </div>
                        </div>
                      ) : canVirtualize ? (
                        <VirtualList
                          ref={listRef as React.Ref<any>}
                          height={Math.min(440, Math.max(220, itemCount * ROW_HEIGHT))}
                          itemCount={itemCount}
                          itemSize={ROW_HEIGHT}
                          width="100%"
                          outerElementType={VirtualizedRowGroup as any}
                        >
                          {({
                            index,
                            style,
                          }: {
                            index: number;
                            style: React.CSSProperties;
                          }) => {
                            const product = items[index];
                            const isPending = pendingSelectionId === product.id;
                            const isDisabled = Boolean(
                              pendingSelectionId && pendingSelectionId !== product.id
                            );
                            const isRowInteractive = isInteractive && !isDisabled && !isPending;

                            const activateRow = () => {
                              if (!isRowInteractive) {
                                return;
                              }
                              void handleRowActivation(product);
                            };

                            return (
                              <div
                                role="row"
                                style={style}
                                className={`${commonRowClassName} last:border-b-0 ${
                                  isInteractive
                                    ? isDisabled
                                      ? "cursor-not-allowed opacity-60"
                                      : "cursor-pointer hover:bg-product-50/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-product-600"
                                    : "cursor-default"
                                }`}
                                tabIndex={
                                  isInteractive ? (isRowInteractive ? 0 : -1) : undefined
                                }
                                onClick={activateRow}
                                onKeyDown={
                                  isInteractive
                                    ? (event) => {
                                        if (event.key === "Enter" || event.key === " ") {
                                          event.preventDefault();
                                          activateRow();
                                        }
                                      }
                                    : undefined
                                }
                                aria-disabled={isPending || undefined}
                                data-testid={`products-modal-row-${product.id}`}
                              >
                                <div
                                  role="cell"
                                  className="truncate"
                                  title={product.ean ?? undefined}
                                >
                                  {product.ean ?? ""}
                                </div>
                                <div
                                  role="cell"
                                  className="truncate font-medium text-slate-900 dark:text-white"
                                  title={product.sku}
                                >
                                  {product.sku}
                                </div>
                                <div
                                  role="cell"
                                  className="flex min-w-0 items-center gap-2"
                                  title={product.name}
                                >
                                  <span className="truncate flex-1">{product.name}</span>
                                  {isInteractive ? (
                                    <span className="inline-flex shrink-0 items-center rounded-full bg-product-100 px-3 py-1 text-xs font-semibold text-product-700 dark:bg-product-700/40 dark:text-product-200">
                                      {isPending ? "Ajout…" : resolvedSelectLabel}
                                    </span>
                                  ) : null}
                                </div>
                              </div>
                            );
                          }}
                        </VirtualList>
                      ) : (
                        items.map((product, index) => {
                          const isPending = pendingSelectionId === product.id;
                          const isDisabled = Boolean(
                            pendingSelectionId && pendingSelectionId !== product.id
                          );
                          const isRowInteractive = isInteractive && !isDisabled && !isPending;

                          const activateRow = () => {
                            if (!isRowInteractive) {
                              return;
                            }
                            void handleRowActivation(product);
                          };

                          return (
                            <div
                              role="row"
                              key={itemKey(index, items)}
                              className={`${commonRowClassName} ${
                                isInteractive
                                  ? isDisabled
                                    ? "cursor-not-allowed opacity-60"
                                    : "cursor-pointer hover:bg-product-50/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-product-600"
                                  : "cursor-default hover:bg-product-50/60 focus-within:bg-product-50/60"
                              }`}
                              tabIndex={
                                isInteractive ? (isRowInteractive ? 0 : -1) : undefined
                              }
                              onClick={activateRow}
                              onKeyDown={
                                isInteractive
                                  ? (event) => {
                                      if (event.key === "Enter" || event.key === " ") {
                                        event.preventDefault();
                                        activateRow();
                                      }
                                    }
                                  : undefined
                              }
                              aria-disabled={isPending || undefined}
                              data-testid={`products-modal-row-${product.id}`}
                            >
                              <div
                                role="cell"
                                className="truncate"
                                title={product.ean ?? undefined}
                              >
                                {product.ean ?? ""}
                              </div>
                              <div
                                role="cell"
                                className="truncate font-medium text-slate-900 dark:text-white"
                                title={product.sku}
                              >
                                {product.sku}
                              </div>
                              <div
                                role="cell"
                                className="flex min-w-0 items-center gap-2"
                                title={product.name}
                              >
                                <span className="truncate flex-1">{product.name}</span>
                                {isInteractive ? (
                                  <span className="inline-flex shrink-0 items-center rounded-full bg-product-100 px-3 py-1 text-xs font-semibold text-product-700 dark:bg-product-700/40 dark:text-product-200">
                                    {isPending ? "Ajout…" : resolvedSelectLabel}
                                  </span>
                                ) : null}
                              </div>
                            </div>
                          );
                        })
                      )}
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <footer className="flex flex-col gap-3 border-t border-slate-200 bg-slate-50/80 px-5 py-4 text-sm text-slate-600 dark:border-slate-700 dark:bg-slate-900/70 dark:text-slate-300 sm:flex-row sm:items-center sm:justify-between">
            <div>
              {data ? (
                <>
                  Page {data.page} / {data.totalPages} — {data.total} éléments
                </>
              ) : (
                "—"
              )}
            </div>
            <div className="flex flex-wrap gap-2">
              <button
                type="button"
                disabled={!data || data.page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="inline-flex items-center gap-1 rounded-full border border-slate-300 px-4 py-1.5 font-medium text-slate-700 transition hover:bg-slate-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-product-600 disabled:cursor-not-allowed disabled:opacity-50 dark:border-slate-600 dark:text-slate-200 dark:hover:bg-slate-800"
              >
                <span aria-hidden="true">←</span>
                <span>Préc.</span>
              </button>
              <button
                type="button"
                disabled={
                  !data || data.totalPages === 0 || data.page >= data.totalPages
                }
                onClick={() => setPage((p) => p + 1)}
                className="inline-flex items-center gap-1 rounded-full border border-product-200 bg-product-600/10 px-4 py-1.5 font-medium text-product-700 transition hover:bg-product-600/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-product-600 disabled:cursor-not-allowed disabled:opacity-50 dark:border-product-700/60 dark:bg-product-700/30 dark:text-product-200 dark:hover:bg-product-700/40"
              >
                <span>Suiv.</span>
                <span aria-hidden="true">→</span>
              </button>
            </div>
          </footer>
        </div>
      </div>
    </div>
  );
}
