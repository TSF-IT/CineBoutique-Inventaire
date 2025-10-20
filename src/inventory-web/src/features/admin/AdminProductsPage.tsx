import { useMemo, useState } from "react";
import { useProductsSearch } from "../../hooks/useProductsSearch";
import { useProductsCount } from "../../hooks/useProductsCount";

export function AdminProductsPage() {
  const [filter, setFilter] = useState("");
  const { rows, loading } = useProductsSearch(filter, 200);
  const { total, loading: loadingTotal } = useProductsCount(0);

  const count = rows.length;
  const [open, setOpen] = useState(count > 0);

  // Ouvre automatiquement quand des données arrivent
  const isOpen = useMemo(() => open || count > 0, [open, count]);

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
          onChange={(e)=>setFilter(e.target.value)}
          placeholder="Filtrer (contains)…"
          aria-label="Filtre produits (contains)"
          style={{ marginLeft: "auto" }}
        />
      </header>

      {isOpen && (
        <div style={{ overflowX: "auto" }}>
          <table style={{ width:"100%", borderCollapse:"collapse" }}>
            <thead>
              <tr>
                <th style={{ textAlign:"left" }}>EAN</th>
                <th style={{ textAlign:"left" }}>SKU</th>
                <th style={{ textAlign:"left" }}>Nom</th>
                <th style={{ textAlign:"left" }}>Groupe</th>
                <th style={{ textAlign:"left" }}>Sous‑groupe</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(p => (
                <tr key={p.sku}>
                  <td>{p.ean ?? ""}</td>
                  <td>{p.sku}</td>
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
