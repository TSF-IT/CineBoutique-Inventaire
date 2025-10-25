import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { mapRowFromCsv, normalizeKey, KNOWN_KEYS } from "./csvMapping";
import {
  CSV_ENCODING_OPTIONS,
  decodeCsvBuffer,
  type CsvEncoding,
} from "./csvEncoding";
import { useShop } from "@/state/ShopContext";

type ErrorItem = { Reason: string; Message?: string; Field?: string };
type SkippedLineInfo = { line: number; raw: string; reason: string };
type DuplicateEntryInfo = { value: string; lines: number[]; rawLines: string[] };
type DuplicateReportInfo = { skus: DuplicateEntryInfo[]; eans: DuplicateEntryInfo[] };
type DryRunPayload = {
  Errors?: ErrorItem[];
  total?: number;
  inserted?: number;
  updated?: number;
  wouldInsert?: number;
  wouldUpdate?: number;
  UnknownColumns?: string[];
  unknownColumns?: string[]; // tolère casse/variante
  skippedLines?: SkippedLineInfo[];
  SkippedLines?: SkippedLineInfo[];
  duplicates?: DuplicateReportInfo;
  Duplicates?: DuplicateReportInfo;
  [k: string]: unknown;
};
type ImportPayload = {
  Errors?: ErrorItem[];
  total?: number;
  errorCount?: number;
  inserted?: number;
  imported?: number; // tolère ancienne clé
  updated?: number;
  wouldInsert?: number;
  wouldUpdate?: number;
  UnknownColumns?: string[];
  unknownColumns?: string[];
  skippedLines?: SkippedLineInfo[];
  SkippedLines?: SkippedLineInfo[];
  duplicates?: DuplicateReportInfo;
  Duplicates?: DuplicateReportInfo;
  [k: string]: unknown;
};

const decodeCsvContent = (result: string | ArrayBuffer | null, forced?: CsvEncoding): string => {
  if (typeof result === "string") {
    return result;
  }

  if (!(result instanceof ArrayBuffer)) {
    return "";
  }

  return decodeCsvBuffer(result, forced).text;
};

const formatSkipReason = (reason: string): string => {
  const key = (reason ?? "").toString().toUpperCase();
  switch (key) {
    case "MISSING_EAN":
      return "EAN/Rfid manquant";
    default:
      return reason || "Motif inconnu";
  }
};

const summarizeSkippedReasons = (lines: SkippedLineInfo[]) => {
  const map = new Map<string, number>();
  lines.forEach((line) => {
    const label = formatSkipReason(line.reason);
    map.set(label, (map.get(label) ?? 0) + 1);
  });
  return Array.from(map.entries()).map(([label, count]) => ({ label, count }));
};

const asRecord = (value: unknown): Record<string, unknown> | null =>
  value && typeof value === "object" ? (value as Record<string, unknown>) : null;

const asArray = (value: unknown): unknown[] =>
  Array.isArray(value) ? value : [];

const normalizeSkippedLines = (payload: unknown): SkippedLineInfo[] => {
  const record = asRecord(payload);
  if (!record) {
    return [];
  }
  const raw = asArray(record.skippedLines ?? record.SkippedLines);
  return raw
    .map((item): SkippedLineInfo | null => {
      const entry = asRecord(item);
      if (!entry) {
        return null;
      }
      const line =
        typeof entry.line === "number"
          ? entry.line
          : typeof entry.Line === "number"
            ? entry.Line
            : 0;
      const rawValue =
        typeof entry.raw === "string"
          ? entry.raw
          : typeof entry.Raw === "string"
            ? entry.Raw
            : "";
      const reasonValue =
        typeof entry.reason === "string"
          ? entry.reason
          : typeof entry.Reason === "string"
            ? entry.Reason
            : "";
      return { line, raw: rawValue, reason: reasonValue };
    })
    .filter(
      (entry): entry is SkippedLineInfo =>
        !!entry && (entry.line > 0 || entry.raw.length > 0 || entry.reason.length > 0)
    );
};

const normalizeDuplicateReport = (payload: unknown): DuplicateReportInfo | null => {
  const record = asRecord(payload);
  if (!record) {
    return null;
  }
  const base = asRecord(record.duplicates ?? record.Duplicates);
  if (!base) {
    return null;
  }

  const mapEntries = (entries: unknown): DuplicateEntryInfo[] => {
    return asArray(entries)
      .map((item): DuplicateEntryInfo | null => {
        const entry = asRecord(item);
        if (!entry) {
          return null;
        }
        const value =
          typeof entry.value === "string"
            ? entry.value
            : typeof entry.Value === "string"
              ? entry.Value
              : "";
        if (!value) {
          return null;
        }
        const linesSource = asArray(entry.lines ?? entry.Lines);
        const rawLinesSource = asArray(entry.rawLines ?? entry.RawLines);
        const lines = linesSource
          .map((v) => (typeof v === "number" ? v : Number(v)))
          .filter((v) => Number.isFinite(v));
        const rawLines = rawLinesSource
          .map((v) =>
            typeof v === "string" ? v : v != null ? String(v) : ""
          )
          .filter((v): v is string => v.length > 0);
        return { value, lines, rawLines };
      })
      .filter(
        (entry: DuplicateEntryInfo | null): entry is DuplicateEntryInfo => entry !== null
      );
  };

  const skus = mapEntries(base.skus ?? base.Skus);
  const eans = mapEntries(base.eans ?? base.Eans);

  if (skus.length === 0 && eans.length === 0) {
    return null;
  }

  return { skus, eans };
};

function parseCsvSemicolon(text: string, maxRows = 10) {
  const lines = text
    .replace(/\r/g, "")
    .split("\n")
    .filter((line) => line.trim().length > 0);
  if (lines.length === 0) return null;
  const rawHeaders = lines[0].split(";").map((segment) => segment.trim());
  const headers = rawHeaders.map(normalizeKey);
  const rows: string[][] = [];
  for (
    let index = 1;
    index < lines.length && rows.length < maxRows;
    index += 1
  ) {
    const columns = lines[index].split(";").map((segment) => segment.trim());
    while (columns.length < rawHeaders.length) {
      columns.push("");
    }
    rows.push(columns.slice(0, rawHeaders.length));
  }
  return { headers, rawHeaders, rows };
}
export function ProductImportPage() {
  const { shop } = useShop();
  const shopId = shop?.id?.trim() ?? "";
  const hasShop = shopId.length > 0;
  const [file, setFile] = useState<File | null>(null);
  const [busyDryRun, setBusyDryRun] = useState(false);
  const [busyImport, setBusyImport] = useState(false);
  const [dryRunRes, setDryRunRes] = useState<DryRunPayload | null>(null);
  const [importRes, setImportRes] = useState<ImportPayload | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<{
    headers: string[];
    rows: string[][];
  } | null>(null);
  const [mappedPreview, setMappedPreview] = useState<{
    headers: string[];
    rows: ReturnType<typeof mapRowFromCsv>[];
  } | null>(null);
  const [fileBuffer, setFileBuffer] = useState<ArrayBuffer | null>(null);
  const [selectedEncoding, setSelectedEncoding] = useState<CsvEncoding>("auto");
  const fileInputRef = useRef<HTMLInputElement>(null);

  const canActions = useMemo(
    () => hasShop && !!file && !busyDryRun && !busyImport,
    [hasShop, file, busyDryRun, busyImport]
  );
  const unknowns = useMemo(() => {
    const arr = dryRunRes?.unknownColumns ?? dryRunRes?.UnknownColumns ?? [];
    return Array.isArray(arr) ? arr : [];
  }, [dryRunRes]);
  const dryRunSkippedLines = useMemo(
    () => normalizeSkippedLines(dryRunRes),
    [dryRunRes]
  );
  const dryRunDuplicates = useMemo(
    () => normalizeDuplicateReport(dryRunRes),
    [dryRunRes]
  );
  const importSkippedLines = useMemo(
    () => normalizeSkippedLines(importRes),
    [importRes]
  );
  const importDuplicates = useMemo(
    () => normalizeDuplicateReport(importRes),
    [importRes]
  );
  const dryRunSummary = useMemo(() => {
    if (!dryRunRes) {
      return null;
    }
    const total =
      typeof dryRunRes.total === "number" ? dryRunRes.total : null;
    const creations =
      typeof dryRunRes.wouldInsert === "number" ? dryRunRes.wouldInsert : null;
    const updates =
      typeof dryRunRes.wouldUpdate === "number" ? dryRunRes.wouldUpdate : null;
    const skipped = dryRunSkippedLines.length;
    const skippedReasons = summarizeSkippedReasons(dryRunSkippedLines);
    const duplicates =
      (dryRunDuplicates?.skus?.length ?? 0) +
      (dryRunDuplicates?.eans?.length ?? 0);
    const imported =
      creations === null && updates === null
        ? null
        : (creations ?? 0) + (updates ?? 0);
    return {
      total,
      creations,
      updates,
      imported,
      skipped,
      skippedReasons,
      duplicates,
      context: "dryRun" as const,
    };
  }, [dryRunRes, dryRunSkippedLines, dryRunDuplicates]);
  const importSummary = useMemo(() => {
    if (!importRes) {
      return null;
    }
    const total =
      typeof importRes.total === "number" ? importRes.total : null;
    const creations =
      typeof importRes.inserted === "number" ? importRes.inserted : null;
    const updates =
      typeof importRes.updated === "number" ? importRes.updated : null;
    const imported =
      creations === null && updates === null
        ? null
        : (creations ?? 0) + (updates ?? 0);
    const skipped = importSkippedLines.length;
    const skippedReasons = summarizeSkippedReasons(importSkippedLines);
    const duplicates =
      (importDuplicates?.skus?.length ?? 0) +
      (importDuplicates?.eans?.length ?? 0);
    return {
      total,
      creations,
      updates,
      imported,
      skipped,
      skippedReasons,
      duplicates,
      context: "import" as const,
    };
  }, [importRes, importSkippedLines, importDuplicates]);
  const renderSkippedLines = useCallback(
    (lines: SkippedLineInfo[], prefix: string): React.ReactNode => {
      if (!lines || lines.length === 0) {
        return null;
      }
      const breakdown = summarizeSkippedReasons(lines)
        .map((entry) => `${entry.count} × ${entry.label}`)
        .join(", ");
      const label =
        lines.length === 1
          ? "1 ligne n'a pas été importée"
          : `${lines.length} lignes n'ont pas été importées`;
      return (
        <div style={{ marginTop: 8 }}>
          <strong>{label}</strong>
          {breakdown && (
            <span style={{ marginLeft: 6, fontStyle: "italic", opacity: 0.8 }}>
              {breakdown}
            </span>
          )}
          <ul style={{ margin: "4px 0 0", paddingLeft: 20 }}>
            {lines.map((line, idx) => {
              const lineContent =
                line.raw && line.raw.length > 0 ? line.raw : "—";
              return (
                <li key={`${prefix}-skip-${line.line}-${idx}`}>
                  Ligne {line.line} =&gt; {lineContent} (
                  {formatSkipReason(line.reason)})
                </li>
              );
            })}
          </ul>
        </div>
      );
    },
    []
  );
  const renderSummaryCounts = useCallback(
    (summary: {
      total: number | null;
      creations?: number | null;
      updates?: number | null;
      imported?: number | null;
      skipped: number;
      skippedReasons: ReturnType<typeof summarizeSkippedReasons>;
      duplicates: number;
      context: "dryRun" | "import";
    }) => {
      const { total, creations, updates, imported, skipped, skippedReasons, duplicates, context } =
        summary;
      const formatCount = (value: number | null | undefined) =>
        typeof value === "number" ? value : "n/a";
      const formatBreakdown = (parts: Array<string | null>) =>
        parts.filter(Boolean).join(" · ");
      const importedLabel =
        context === "dryRun" ? "Importations prévues" : "Importées";
      const creationLabel = context === "dryRun" ? "Créations prévues" : "Créations";
      const updateLabel = context === "dryRun" ? "Mises à jour prévues" : "Mises à jour";
      const importedParts: string[] = [];
      if (typeof creations === "number") {
        importedParts.push(
          `${creations} ${creationLabel.toLowerCase()}${creations === 1 ? "" : "s"}`
        );
      }
      if (typeof updates === "number") {
        importedParts.push(
          `${updates} ${updateLabel.toLowerCase()}${updates === 1 ? "" : "s"}`
        );
      }
      const skippedDetails = skippedReasons
        .map((entry) => `${entry.count} × ${entry.label}`)
        .join(", ");
      return (
        <div style={{ display: "grid", gap: 4, marginTop: 8 }}>
          <div>
            <strong>Total lignes</strong> : {formatCount(total)}
          </div>
          {typeof imported === "number" && (
            <div>
              <strong>{importedLabel}</strong> : {imported}
              {importedParts.length > 0 && (
                <span style={{ marginLeft: 6, fontStyle: "italic", opacity: 0.8 }}>
                  {formatBreakdown(importedParts)}
                </span>
              )}
            </div>
          )}
          <div>
            <strong>Lignes ignorées</strong> : {skipped}
            {skipped > 0 && skippedDetails && (
              <span style={{ marginLeft: 6, fontStyle: "italic", opacity: 0.8 }}>
                {skippedDetails}
              </span>
            )}
          </div>
          <div>
            <strong>Doublons détectés</strong> :{" "}
            {duplicates > 0 ? `${duplicates} (détails ci-dessous)` : "Aucun"}
          </div>
        </div>
      );
    },
    []
  );
  const renderDuplicateSummary = useCallback(
    (report: DuplicateReportInfo | null, prefix: string): React.ReactNode => {
      if (
        !report ||
        (report.skus.length === 0 && report.eans.length === 0)
      ) {
        return null;
      }
      return (
        <div style={{ marginTop: 8 }}>
          <strong>Doublons détectés (importés quand même)</strong>
          <div style={{ marginTop: 4, display: "grid", gap: 4 }}>
            {report.skus.length > 0 && (
              <div key={`${prefix}-dup-sku`}>
                <span style={{ fontStyle: "italic" }}>SKU / Item</span>
                <ul style={{ margin: "4px 0 0", paddingLeft: 20 }}>
                  {report.skus.map((entry, idx) => {
                    const lineSummary =
                      entry.lines.length > 0
                        ? entry.lines.join(", ")
                        : "?";
                    return (
                      <li
                        key={`${prefix}-dup-sku-${entry.value}-${idx}`}
                        title={entry.rawLines.join("\n")}
                      >
                        <code>{entry.value}</code> — lignes {lineSummary}
                      </li>
                    );
                  })}
                </ul>
              </div>
            )}
            {report.eans.length > 0 && (
              <div key={`${prefix}-dup-ean`}>
                <span style={{ fontStyle: "italic" }}>EAN / RFID</span>
                <ul style={{ margin: "4px 0 0", paddingLeft: 20 }}>
                  {report.eans.map((entry, idx) => {
                    const lineSummary =
                      entry.lines.length > 0
                        ? entry.lines.join(", ")
                        : "?";
                    return (
                      <li
                        key={`${prefix}-dup-ean-${entry.value}-${idx}`}
                        title={entry.rawLines.join("\n")}
                      >
                        <code>{entry.value}</code> — lignes {lineSummary}
                      </li>
                    );
                  })}
                </ul>
              </div>
            )}
          </div>
        </div>
      );
    },
    []
  );

  const processBuffer = useCallback(
    (buffer: ArrayBuffer | null, encoding: CsvEncoding) => {
      if (!buffer) {
        setPreview(null);
        setMappedPreview(null);
        setDryRunRes(null);
        setImportRes(null);
        setError(null);
        return;
      }
      const text = decodeCsvContent(
        buffer,
        encoding === "auto" ? undefined : encoding
      );
      const parsed = parseCsvSemicolon(text, 10);
      if (!parsed) {
        setPreview(null);
        setMappedPreview(null);
        setDryRunRes(null);
        setImportRes(null);
        setError(null);
        return;
      }
      setPreview({ headers: parsed.rawHeaders, rows: parsed.rows });
      const mapped = parsed.rows.map((row) =>
        mapRowFromCsv(parsed.headers, row)
      );
      setMappedPreview({ headers: parsed.headers, rows: mapped });
      setDryRunRes(null);
      setImportRes(null);
      setError(null);
    },
    []
  );

  function onPickFile(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files && e.target.files[0] ? e.target.files[0] : null;
    setFile(f);
    setSelectedEncoding("auto");
    if (f) {
      const reader = new FileReader();
      reader.onload = () => {
        const buffer =
          reader.result instanceof ArrayBuffer ? reader.result : null;
        setFileBuffer(buffer);
        processBuffer(buffer, "auto");
      };
      reader.readAsArrayBuffer(f);
    } else {
      setPreview(null);
      setMappedPreview(null);
      setDryRunRes(null);
      setImportRes(null);
      setError(null);
      setFileBuffer(null);
      setSelectedEncoding("auto");
    }
  }

  useEffect(() => {
    if (fileBuffer) {
      processBuffer(fileBuffer, selectedEncoding);
    }
  }, [fileBuffer, selectedEncoding, processBuffer]);

  async function postCsv(dryRun: boolean): Promise<unknown> {
    if (!hasShop) throw new Error("Sélectionnez une entité avant d'importer.");
    if (!file) throw new Error("Aucun fichier sélectionné");
    const uploadBuffer = await file.arrayBuffer();
    const decoded = decodeCsvBuffer(uploadBuffer, selectedEncoding);
    const normalizedFile = new File(
      [decoded.text],
      file.name,
      { type: "text/csv;charset=utf-8" },
    );
    const form = new FormData();
    form.append("file", normalizedFile, file.name);
    const q = new URLSearchParams({ dryRun: String(dryRun) }).toString();
    const res = await fetch(
      `/api/shops/${encodeURIComponent(shopId)}/products/import?${q}`,
      { method: "POST", body: form }
    );
    const text = await res.text();
    let payload: unknown = null;
    try {
      payload = text ? JSON.parse(text) : null;
    } catch {
      /* corps non JSON */
    }
    if (!res.ok) {
      // Essaye d’extraire un message utile
      const record = asRecord(payload);
      let message: string | undefined;
      if (record) {
        if (Array.isArray(record.Errors) && record.Errors.length > 0) {
          const firstError = asRecord(record.Errors[0]);
          if (firstError && typeof firstError.Message === "string") {
            message = firstError.Message;
          }
        }
        if (!message && typeof record.title === "string") {
          message = record.title;
        }
        if (!message && typeof record.detail === "string") {
          message = record.detail;
        }
      }
      const fallbackMessage = `HTTP ${res.status}`;
      throw new Error(message ?? fallbackMessage);
    }
    return payload;
  }

  async function onDryRun() {
    if (!hasShop) {
      setError("Sélectionnez une entité avant de lancer un dry-run.");
      return;
    }
    if (!file) {
      setError("Sélectionnez un fichier CSV.");
      return;
    }
    setBusyDryRun(true);
    setError(null);
    setDryRunRes(null);
    setImportRes(null);
    try {
      const payload = await postCsv(true);
      setDryRunRes(payload as DryRunPayload);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusyDryRun(false);
    }
  }

  async function onImport() {
    if (!hasShop) {
      setError("Sélectionnez une entité avant d'importer.");
      return;
    }
    if (!file) {
      setError("Sélectionnez un fichier CSV.");
      return;
    }
    setBusyImport(true);
    setError(null);
    setImportRes(null);
    try {
      const payload = await postCsv(false);
      setImportRes(payload as ImportPayload);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusyImport(false);
    }
  }

  return (
    <section style={{ display: "grid", gap: 12, maxWidth: 900 }}>
      <h2 style={{ margin: 0 }}>Import produits (CSV)</h2>

      <div style={{ display: "grid", gap: 8 }}>
        {!hasShop && (
          <div style={{ color: "#b00020" }}>
            Sélectionnez d&apos;abord une entité (shop) pour accéder à
            l&apos;import produit.
          </div>
        )}
        <input
          ref={fileInputRef}
          type="file"
          accept=".csv,text/csv"
          onChange={onPickFile}
          aria-label="Fichier CSV à importer"
        />
        <label style={{ display: "grid", gap: 4, maxWidth: 360 }}>
          <span
            style={{
              fontSize: 12,
              fontWeight: 600,
              textTransform: "uppercase",
              letterSpacing: 0.5,
              color: "var(--cb-muted, #4b5563)",
            }}
          >
            Encodage du fichier
          </span>
          <select
            value={selectedEncoding}
            onChange={(event) =>
              setSelectedEncoding(event.target.value as CsvEncoding)
            }
            style={{
              padding: "8px 12px",
              borderRadius: 12,
              border: "1px solid var(--cb-border-soft, #d1d5db)",
              background: "var(--cb-surface-soft, #fff)",
              fontSize: 14,
            }}
          >
            {CSV_ENCODING_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>
        <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
          <button type="button" onClick={onDryRun} disabled={!canActions}>
            {busyDryRun ? "Dry‑run…" : "Prévisualiser (dry‑run)"}
          </button>
          <button
            type="button"
            onClick={onImport}
            disabled={!canActions || !dryRunRes}
          >
            {busyImport ? "Import…" : "Importer"}
          </button>
          <button
            type="button"
            onClick={() => {
              setFile(null);
              setFileBuffer(null);
              setSelectedEncoding("auto");
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
          Astuce&nbsp;: le serveur s’attend à un CSV **séparé par “;”** avec des
          colonnes connues (ex. <code>sku</code>, <code>ean</code>,{" "}
          <code>name</code>, <code>groupe</code>, <code>sous_groupe</code> ou
          leurs synonymes).
        </small>
      </div>

      {error && <div style={{ color: "#b00020" }}>Erreur&nbsp;: {error}</div>}

      {preview && (
        <div style={{ border: "1px solid #ddd", padding: 12, borderRadius: 6 }}>
          <h3 style={{ marginTop: 0 }}>Prévisualisation brute (local)</h3>
          <small style={{ display: "block", opacity: 0.75, marginBottom: 8 }}>
            Les 10 premières lignes du fichier sélectionné. Les colonnes connues
            sont surlignées.
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
                        title={
                          isKnown
                            ? `Colonne reconnue (${normalized})`
                            : "Colonne inconnue"
                        }
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
                      <td
                        key={j}
                        style={{
                          padding: "4px 6px",
                          borderTop: "1px solid #eee",
                        }}
                      >
                        {cell}
                      </td>
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
            Les colonnes sont normalisées comme côté serveur (synonymes → clefs
            canoniques). Les colonnes non reconnues seront fusionnées dans{" "}
            <code>Attributes</code> lors de l’import.
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
                      <td
                        title={
                          keys.length
                            ? keys
                                .map(
                                  (k) => `${k}: ${r.attributes[k] ?? "null"}`
                                )
                                .join("\n")
                            : "—"
                        }
                      >
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
          <div
            style={{
              display: "flex",
              gap: 6,
              alignItems: "center",
              flexWrap: "wrap",
            }}
          >
            <strong>Colonnes inconnues :</strong>
            {unknowns.length === 0 ? (
              <span>Aucune</span>
            ) : (
              unknowns.map((u) => (
                <span
                  key={u}
                  style={{
                    border: "1px solid #ccc",
                    borderRadius: 12,
                    padding: "2px 8px",
                  }}
                >
                  {u}
                </span>
              ))
            )}
          </div>
          {dryRunSummary && renderSummaryCounts(dryRunSummary)}
          {renderSkippedLines(dryRunSkippedLines, "dryRun")}
          {renderDuplicateSummary(dryRunDuplicates, "dryRun")}
          <details style={{ marginTop: 8 }}>
            <summary>Payload serveur (JSON)</summary>
            <pre style={{ whiteSpace: "pre-wrap" }}>
              {JSON.stringify(dryRunRes, null, 2)}
            </pre>
          </details>
        </div>
      )}

      {importRes && (
        <div style={{ border: "1px solid #ddd", padding: 12, borderRadius: 6 }}>
          <h3 style={{ marginTop: 0 }}>Résultat import</h3>
          {importSummary && renderSummaryCounts(importSummary)}
          {typeof importRes.errorCount === "number" && (
            <div style={{ marginTop: 8 }}>
              <strong>Erreurs</strong> : {importRes.errorCount}
            </div>
          )}
          {renderSkippedLines(importSkippedLines, "import")}
          {renderDuplicateSummary(importDuplicates, "import")}
          {Array.isArray(importRes.Errors) && importRes.Errors.length > 0 && (
            <div style={{ marginTop: 8 }}>
              <strong>Erreurs :</strong>
              <ul>
                {importRes.Errors.map((e, i) => (
                  <li key={i}>
                    <code>{e.Reason}</code>
                    {e.Field ? ` sur ${e.Field}` : ""}
                    {e.Message ? ` — ${e.Message}` : ""}
                  </li>
                ))}
              </ul>
            </div>
          )}
          <details style={{ marginTop: 8 }}>
            <summary>Payload serveur (JSON)</summary>
            <pre style={{ whiteSpace: "pre-wrap" }}>
              {JSON.stringify(importRes, null, 2)}
            </pre>
          </details>
        </div>
      )}
    </section>
  );
}
