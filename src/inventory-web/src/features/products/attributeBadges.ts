export type Badges = { key: string; label: string; value: string }[];

const LABELS: Record<string, string> = {
  packaging: "Packaging",
  origine: "Origine",
  couleurSecondaire: "Couleur",
};

export function extractBadges(attributes: Record<string, any> | null | undefined): Badges {
  if (!attributes || typeof attributes !== "object") return [];
  const out: Badges = [];
  for (const [k, v] of Object.entries(attributes)) {
    if (!(k in LABELS)) continue;
    const s = v == null ? "" : typeof v === "string" ? v : JSON.stringify(v);
    if (!s) continue;
    out.push({ key: k, label: LABELS[k], value: s });
  }
  return out;
}
