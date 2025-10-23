import React, { useMemo, useRef, useState } from "react";
import { useShop } from "@/state/ShopContext";
import { mapRowFromCsv, normalizeKey, KNOWN_KEYS } from "./csvMapping";

type ErrorItem = { Reason: string; Message?: string; Field?: string };
type DryRunPayload = {
  Errors?: ErrorItem[];
  UnknownColumns?: string[];
  unknownColumns?: string[]; // tolère casse/variante
  [k: string]: any;
};
type ImportPayload = {
  Errors?: ErrorItem[];
  errorCount?: number;
  inserted?: number;
  imported?: number; // tolère ancienne clé
  UnknownColumns?: string[];
  unknownColumns?: string[];
  [k: string]: any;
};

function parseCsvSemicolon(text: string, maxRows = 10): { headers: string[]; rows: string[][] } | null {
  const lines = text.replace(/\r/g, "").split("\n").filter(l => l.trim().length > 0);
  if (lines.length === 0) return null;
  const headers = lines[0].split(";").map(s => s.trim());
  const rows: string[][] = [];
  for (let i = 1; i < lines.length && rows.length < maxRows; i++) {
    const cols = lines[i].split(";").map(s => s.trim());
    while (cols.length < headers.length) cols.push("");
    rows.push(cols.slice(0, headers.length));
  }
  return { headers, rows };
}

export function ProductImportPage() {
  const { shop } = useShop();
  const [file, setFile] = useState<File | null>(null);
  const [busyDryRun, setBusyDryRun] = useState(false);
  const [busyImport, setBusyImport] = useState(false);
  const [dryRunRes, setDryRunRes] = useState<DryRunPayload | null>(null);
  const [importRes, setImportRes] = useState<ImportPayload | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<{ headers: string[]; rows: string[][] } | null>(null);
  const [mappedPreview, setMappedPreview] = useState<{ headers: string[]; rows: ReturnType<typeof mapRowFromCsv>[] } | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const canActions = useMemo(
    () => !!file && !busyDryRun && !busyImport && !!shop?.id,
    [file, busyDryRun, busyImport, shop?.id],
  );
  const unknowns = useMemo(() => {
    const arr = dryRunRes?.unknownColumns ?? dryRunRes?.UnknownColumns ?? [];
    return Array.isArray(arr) ? arr : [];
  }, [dryRunRes]);
  const inserted = useMemo(() => {
    const v = importRes?.inserted ?? importRes?.imported ?? null;
    return typeof v === "number" ? v : null;
  }, [importRes]);

  function parseCsvSemicolon(text: string, maxRows = 10) {
    const lines = text.replace(/\r/g, "").split("\n").filter(l => l.trim().length > 0);
    if (lines.length === 0) return null;
    const rawHeaders = lines[0].split(";").map(s => s.trim());
    const headers = rawHeaders.map(normalizeKey);
    const rows: string[][] = [];
    for (let i = 1; i < lines.length && rows.length < maxRows; i++) {
      const cols = lines[i].split(";").map(s => s.trim());
      while (cols.length < rawHeaders.length) cols.push("");
      rows.push(cols.slice(0, rawHeaders.length));
    }
    return { headers, rawHeaders, rows };
  }

  function onPickFile(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files && e.target.files[0] ? e.target.files[0] : null;
    setFile(f);
    if (f) {
      const reader = new FileReader();
      reader.onload = () => {
        const txt = typeof reader.result === "string" ? reader.result : "";
        const parsed = parseCsvSemicolon(txt, 10);
        if (!parsed) {
          setPreview(null);
          setMappedPreview(null);
          setDryRunRes(null);
          setImportRes(null);
          setError(null);
          return;
        }

        // Aperçu brut (déjà en place chez toi)
        setPreview({ headers: parsed.rawHeaders, rows: parsed.rows });

        // Aperçu mappé
        const mapped = parsed.rows.map(row => mapRowFromCsv(parsed.headers, row));
        setMappedPreview({ headers: parsed.headers, rows: mapped });

        // reset des résultats serveurs
        setDryRunRes(null); setImportRes(null); setError(null);
      };
      reader.readAsText(f, "utf-8");
    } else {
      setPreview(null);
      setMappedPreview(null);
      setDryRunRes(null);
      setImportRes(null);
      setError(null);
    }
  }

  async function postCsv(dryRun: boolean): Promise<any> {
    if (!file) throw new Error("Aucun fichier sélectionné");
    const form = new FormData();
    form.append("file", file, file.name);
    const q = new URLSearchParams({ dryRun: String(dryRun) }).toString();
    if (!shop?.id) {
      throw new Error("Aucune boutique sélectionnée.");
    }

    const endpoint = `/api/shops/${shop.id}/products/import`;
    const res = await fetch(`${endpoint}?${q}`, { method: "POST", body: form });
    const text = await res.text();
    let payload: any = null;
    try { payload = text ? JSON.parse(text) : null; } catch { /* corps non JSON */ }
    if (!res.ok) {
      // Essaye d’extraire un message utile
      const msg =
        (payload?.Errors && Array.isArray(payload.Errors) && payload.Errors[0]?.Message) ||
        (payload?.title ?? payload?.detail) ||
        `HTTP ${res.status}`;
      throw new Error(msg);
    }
    return payload;
  }

  async function onDryRun() {
    if (!file) { setError("Sélectionnez un fichier CSV."); return; }
    setBusyDryRun(true); setError(null); setDryRunRes(null); setImportRes(null);
    try {
      const payload = await postCsv(true);
      setDryRunRes(payload as DryRunPayload);
    } catch (e: any) {
      setError(e?.message || String(e));
    } finally {
      setBusyDryRun(false);
    }
  }

  async function onImport() {
    if (!file) { setError("Sélectionnez un fichier CSV."); return; }
    setBusyImport(true); setError(null); setImportRes(null);
    try {
      const payload = await postCsv(false);
      setImportRes(payload as ImportPayload);
    } catch (e: any) {
      setError(e?.message || String(e));
    } finally {
      setBusyImport(false);
    }
  }

  return (
    <section style={{ display: "grid", gap: 12, maxWidth: 900 }}>
      <h2 style={{ margin: 0 }}>Import produits (CSV)</h2>

      <div style={{ display: "grid", gap: 8 }}>
        <input
          ref={fileInputRef}
          type="file"
          accept=".csv,text/csv"
          onChange={onPickFile}
          aria-label="Fichier CSV à importer"
        />
        <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
          <button type="button" onClick={onDryRun} disabled={!canActions}>
            {busyDryRun ? "Dry‑run…" : "Prévisualiser (dry‑run)"}
          </button>
          <button type="button" onClick={onImport} disabled={!canActions || !dryRunRes}>
            {busyImport ? "Import…" : "Importer"}
          </button>
          <button
            type="button"
            onClick={() => {
              setFile(null);
              if (fileInputRef.current) fileInputRef.current.value = "";
              setPreview(null);
              setMappedPreview(null);
              setDryRunRes(null);
              setImportRes(null);
              setError(null);
            }}
            disabled={busyDryRun || busyImport}
          >
            Réinitialiser
          </button>
        </div>
        <small style={{ opacity: 0.75 }}>
          Astuce&nbsp;: le serveur s’attend à un CSV **séparé par “;”** avec des colonnes connues
          (ex. <code>sku</code>, <code>ean</code>, <code>name</code>, <code>groupe</code>, <code>sous_groupe</code> ou leurs synonymes).
        </small>
      </div>

      {error && <div style={{ color: "#b00020" }}>Erreur&nbsp;: {error}</div>}

      {preview && (
        <div style={{ border: "1px solid #ddd", padding: 12, borderRadius: 6 }}>
          <h3 style={{ marginTop: 0 }}>Prévisualisation brute (local)</h3>
          <small style={{ display: "block", opacity: 0.75, marginBottom: 8 }}>
            Les 10 premières lignes du fichier sélectionné. Les colonnes connues sont surlignées.
          </small>
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  {preview.headers.map((h, i) => {
                    const normalized = normalizeKey(h);
                    const isKnown = KNOWN_KEYS.has(normalized);
                    return (
                      <th
                        key={i}
                        style={{
                          textAlign: "left",
                          backgroundColor: isKnown ? "#f5f5f5" : undefined,
                          padding: "4px 6px",
                        }}
                        title={isKnown ? `Colonne reconnue (${normalized})` : "Colonne inconnue"}
                      >
                        {h}
                      </th>
                    );
                  })}
                </tr>
              </thead>
              <tbody>
                {preview.rows.map((row, i) => (
                  <tr key={i}>
                    {row.map((cell, j) => (
                      <td key={j} style={{ padding: "4px 6px", borderTop: "1px solid #eee" }}>{cell}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {mappedPreview && (
        <div style={{ border: "1px solid #ddd", padding: 12, borderRadius: 6 }}>
          <h3 style={{ marginTop: 0 }}>Prévisualisation mappée (local)</h3>
          <small style={{ display: "block", opacity: 0.75, marginBottom: 8 }}>
            Les colonnes sont normalisées comme côté serveur (synonymes → clefs canoniques). Les colonnes non reconnues
            seront fusionnées dans <code>Attributes</code> lors de l’import.
          </small>
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th style={{ textAlign: "left" }}>SKU</th>
                  <th style={{ textAlign: "left" }}>EAN</th>
                  <th style={{ textAlign: "left" }}>Nom</th>
                  <th style={{ textAlign: "left" }}>Groupe</th>
                  <th style={{ textAlign: "left" }}>Sous-groupe</th>
                  <th style={{ textAlign: "left" }}>Attributs</th>
                </tr>
              </thead>
              <tbody>
                {mappedPreview.rows.map((r, i) => {
                  const keys = Object.keys(r.attributes);
                  return (
                    <tr key={i}>
                      <td>{r.sku}</td>
                      <td>{r.ean}</td>
                      <td>{r.name}</td>
                      <td>{r.groupe}</td>
                      <td>{r.sousGroupe}</td>
                      <td title={keys.length ? keys.map(k => `${k}: ${r.attributes[k] ?? "null"}`).join("\n") : "—"}>
                        {keys.length ? `${keys.length} attribut(s)` : "—"}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {dryRunRes && (
        <div style={{ border: "1px solid #ddd", padding: 12, borderRadius: 6 }}>
          <h3 style={{ marginTop: 0 }}>Prévisualisation (dry‑run)</h3>
          <div style={{ display: "flex", gap: 6, alignItems: "center", flexWrap: "wrap" }}>
            <strong>Colonnes inconnues :</strong>
            {unknowns.length === 0 ? <span>Aucune</span> : unknowns.map(u => (
              <span key={u} style={{ border: "1px solid #ccc", borderRadius: 12, padding: "2px 8px" }}>{u}</span>
            ))}
          </div>
          <details style={{ marginTop: 8 }}>
            <summary>Payload serveur (JSON)</summary>
            <pre style={{ whiteSpace: "pre-wrap" }}>{JSON.stringify(dryRunRes, null, 2)}</pre>
          </details>
        </div>
      )}

      {importRes && (
        <div style={{ border: "1px solid #ddd", padding: 12, borderRadius: 6 }}>
          <h3 style={{ marginTop: 0 }}>Résultat import</h3>
          <div style={{ display: "grid", gap: 4 }}>
            <div><strong>Insérés</strong> : {inserted ?? "n/a"}</div>
            {(typeof importRes.errorCount === "number") && (
              <div><strong>Erreurs</strong> : {importRes.errorCount}</div>
            )}
          </div>
          {Array.isArray(importRes.Errors) && importRes.Errors.length > 0 && (
            <div style={{ marginTop: 8 }}>
              <strong>Erreurs :</strong>
              <ul>
                {importRes.Errors.map((e, i) => (
                  <li key={i}>
                    <code>{e.Reason}</code>{e.Field ? ` sur ${e.Field}` : ""}{e.Message ? ` — ${e.Message}` : ""}
                  </li>
                ))}
              </ul>
            </div>
          )}
          <details style={{ marginTop: 8 }}>
            <summary>Payload serveur (JSON)</summary>
            <pre style={{ whiteSpace: "pre-wrap" }}>{JSON.stringify(importRes, null, 2)}</pre>
          </details>
        </div>
      )}
    </section>
  );
}
