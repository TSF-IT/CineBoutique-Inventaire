import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { extractBadges } from "./attributeBadges";

type Details = {
  sku: string; ean?: string | null; name: string;
  group?: string | null; subGroup?: string | null;
  attributes?: Record<string, any> | null;
};

export function ProductDetailsPage() {
  const { sku = "" } = useParams();
  const [data, setData] = useState<Details | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    setLoading(true); setError(null);
    fetch(`/api/products/${encodeURIComponent(sku)}/details`)
      .then(async r => {
        if (!alive) return;
        if (r.status === 404) { setError("Produit introuvable"); setData(null); return; }
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        const js = await r.json();
        setData(js ?? null);
      })
      .catch(e => { if (alive) setError(e.message || String(e)); })
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, [sku]);

  const attrs = data?.attributes && typeof data.attributes === "object" ? data.attributes : null;
  const badges = extractBadges(attrs);

  return (
    <section style={{ display:"grid", gap: 12, maxWidth: 900 }}>
      <h2 style={{ margin:0 }}>Produit {data?.sku ?? sku}</h2>
      {loading && <div>Chargement…</div>}
      {error && <div style={{ color:"#b00020" }}>Erreur : {error}</div>}
      {!loading && !error && data && (
        <>
          <div><strong>Nom</strong> : {data.name}</div>
          <div><strong>EAN</strong> : {data.ean ?? "—"}</div>
          <div><strong>Groupe</strong> : {data.group ?? "—"}{data.subGroup ? ` / ${data.subGroup}` : ""}</div>
          {badges.length > 0 && (
            <div style={{ display:"flex", gap:8, flexWrap:"wrap" }}>
              {badges.map(b => (
                <span key={b.key}
                  style={{
                    display:"inline-flex", alignItems:"center", gap:6,
                    padding:"2px 10px", borderRadius:999, background:"#eef", border:"1px solid #cde"
                  }}>
                  <strong>{b.label} :</strong> {b.value}
                </span>
              ))}
            </div>
          )}
          <div>
            <strong>Attributs</strong> :
            {attrs && Object.keys(attrs).length > 0 ? (
              <ul>
                {Object.entries(attrs).map(([k,v]) => (
                  <li key={k}><code>{k}</code> : {typeof v === "string" ? v : JSON.stringify(v)}</li>
                ))}
              </ul>
            ) : <span> aucun</span>}
          </div>
        </>
      )}
    </section>
  );
}
