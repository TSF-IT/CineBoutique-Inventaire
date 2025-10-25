import { useMemo, useState } from "react";
import { useProductsSearch } from "../../hooks/useProductsSearch";
import { useProductsCount } from "../../hooks/useProductsCount";

export function AdminProductsPage() {
  const [filter, setFilter] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [sortKey, setSortKey] = useState<"sku"|"name"|"ean">("name");
  const [sortDir, setSortDir] = useState<"asc"|"desc">("asc");
  const { rows, loading } = useProductsSearch(filter, 200, { page, pageSize, sortKey, sortDir });
  const { total, loading: loadingTotal } = useProductsCount(0);

  const count = rows.length;
  const [open, setOpen] = useState(count > 0);

  function onSort(next: "sku"|"name"|"ean") {
    setSortKey(k => {
      if (k === next) {
        setSortDir(d => (d === "asc" ? "desc" : "asc"));
        return k;
      }
      setSortDir("asc");
      return next;
    });
    setPage(1);
  }

  // Ouvre automatiquement quand des données arrivent
  const isOpen = useMemo(() => open || count > 0, [open, count]);

  const getAriaSort = (column: "sku" | "name" | "ean"): "none" | "ascending" | "descending" => {
    if (sortKey !== column) {
      return "none";
    }
    return sortDir === "asc" ? "ascending" : "descending";
  };

  return (
    <section style={{ display: "grid", gap: 12 }}>
      <header style={{ display:"flex", alignItems:"center", gap:12 }}>
        <h2 style={{ margin:0 }}>Produits</h2>
        <button
          type="button"
          onClick={()=>setOpen(o=>!o)}
          title="Afficher/masquer la liste"
          style={{ borderRadius: 12, padding: "2px 10px", background:"#eee", border:"1px solid #ccc" }}
        >
          {loading || loadingTotal ? "…" : `${rows.length}${total !== null ? ` / ${total}` : ""}`} produits
        </button>
        <input
          value={filter}
          onChange={(e)=>{
            setFilter(e.target.value);
            setPage(1);
          }}
          placeholder="Filtrer (contains)…"
          aria-label="Filtre produits (contains)"
          style={{ marginLeft: "auto" }}
        />
      </header>

      {isOpen && (
        <div style={{ overflowX: "auto" }}>
          <div style={{ display:"flex", gap:8, alignItems:"center", marginBottom:8 }}>
            <button type="button" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>Préc.</button>
            <span>Page {page}</span>
            <button type="button" onClick={() => setPage(p => p + 1)} disabled={rows.length < pageSize}>Suiv.</button>
            <span style={{ marginLeft: 12 }}>Taille :</span>
            <select value={pageSize} onChange={(e)=>{ setPage(1); setPageSize(parseInt(e.target.value,10)||25); }}>
              <option value={10}>10</option>
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
            </select>
          </div>
          <table style={{ width:"100%", borderCollapse:"collapse" }}>
            <thead>
              <tr>
                <th style={{ textAlign:"left", cursor:"pointer" }} aria-sort={getAriaSort("ean")} onClick={()=>onSort("ean")}>
                  EAN {sortKey==="ean" ? (sortDir==="asc"?"▲":"▼") : ""}
                </th>
                <th style={{ textAlign:"left", cursor:"pointer" }} aria-sort={getAriaSort("sku")} onClick={()=>onSort("sku")}>
                  SKU {sortKey==="sku" ? (sortDir==="asc"?"▲":"▼") : ""}
                </th>
                <th style={{ textAlign:"left", cursor:"pointer" }} aria-sort={getAriaSort("name")} onClick={()=>onSort("name")}>
                  Nom {sortKey==="name" ? (sortDir==="asc"?"▲":"▼") : ""}
                </th>
                <th style={{ textAlign:"left" }}>Groupe</th>
                <th style={{ textAlign:"left" }}>Sous‑groupe</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(p => (
                <tr key={p.sku}>
                  <td>{p.ean ?? ""}</td>
                  <td>
                    <a href={`/products/${encodeURIComponent(p.sku)}`}>{p.sku}</a>
                  </td>
                  <td>{p.name}</td>
                  <td>{p.group ?? ""}</td>
                  <td>{p.subGroup ?? ""}</td>
                </tr>
              ))}
              {!loading && rows.length===0 && (
                <tr><td colSpan={5} style={{ opacity:0.7 }}>Aucun produit</td></tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
