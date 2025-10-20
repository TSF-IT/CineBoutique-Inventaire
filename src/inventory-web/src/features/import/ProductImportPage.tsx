import React, { useMemo, useRef, useState } from "react";

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

export function ProductImportPage() {
  const [file, setFile] = useState<File | null>(null);
  const [busyDryRun, setBusyDryRun] = useState(false);
  const [busyImport, setBusyImport] = useState(false);
  const [dryRunRes, setDryRunRes] = useState<DryRunPayload | null>(null);
  const [importRes, setImportRes] = useState<ImportPayload | null>(null);
  const [error, setError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const canActions = useMemo(() => !!file && !busyDryRun && !busyImport, [file, busyDryRun, busyImport]);
  const unknowns = useMemo(() => {
    const arr = dryRunRes?.unknownColumns ?? dryRunRes?.UnknownColumns ?? [];
    return Array.isArray(arr) ? arr : [];
  }, [dryRunRes]);
  const inserted = useMemo(() => {
    const v = importRes?.inserted ?? importRes?.imported ?? null;
    return typeof v === "number" ? v : null;
  }, [importRes]);

  function onPickFile(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files && e.target.files[0] ? e.target.files[0] : null;
    setFile(f);
    // reset états de prévisualisation pour éviter toute confusion
    setDryRunRes(null);
    setImportRes(null);
    setError(null);
  }

  async function postCsv(dryRun: boolean): Promise<any> {
    if (!file) throw new Error("Aucun fichier sélectionné");
    const form = new FormData();
    form.append("file", file, file.name);
    const q = new URLSearchParams({ dryRun: String(dryRun) }).toString();
    const res = await fetch(`/api/products/import?${q}`, { method: "POST", body: form });
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
            onClick={() => { setFile(null); if (fileInputRef.current) fileInputRef.current.value = ""; setDryRunRes(null); setImportRes(null); setError(null); }}
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
