export const KNOWN_KEYS = new Set(["sku","ean","name","groupe","sousGroupe"]);
export const SYNONYMS: Record<string,string> = {
  "barcode_rfid": "ean",
  "ean13": "ean",
  "item": "sku",
  "code": "sku",
  "descr": "name",
  "description": "name",
  "sous_groupe": "sousGroupe",
  "subgroup": "sousGroupe",
  "groupe": "groupe",
};

export function normalizeKey(raw: string): string {
  const k = (raw ?? "").trim();
  if (!k) return k;
  const lower = k.toLowerCase();
  // on mappe en conservant la casse canonique sur les clés connues
  const mapped = SYNONYMS[lower];
  if (mapped) return mapped;
  // si la clé est déjà une clé connue mais en casse différente
  if (KNOWN_KEYS.has(lower)) {
    switch (lower) {
      case "sousgroupe": return "sousGroupe";
      case "ean": return "ean";
      case "sku": return "sku";
      case "name": return "name";
      case "groupe": return "groupe";
    }
  }
  return k; // inconnue, on garde tel quel (ira dans Attributes)
}

export type MappedRow = {
  sku: string; ean: string; name: string; groupe: string; sousGroupe: string;
  attributes: Record<string, string | null>;
};

export function mapRowFromCsv(
  headers: string[],
  values: string[]
): MappedRow {
  const canon: Record<string,string> = {};
  headers.forEach((h, i) => {
    const key = normalizeKey(h);
    const val = (values[i] ?? "").trim();
    if (!(key in canon)) canon[key] = val;
  });

  const attributes: Record<string, string | null> = {};
  for (const [k, v] of Object.entries(canon)) {
    if (!KNOWN_KEYS.has(k.toLowerCase())) {
      attributes[k] = v === "" ? null : v;
    }
  }

  const sku = canon["sku"] ?? "";
  const ean = canon["ean"] ?? "";
  const name = canon["name"] ?? "";
  const groupe = canon["groupe"] ?? "";
  const sousGroupe = canon["sousGroupe"] ?? "";

  return { sku, ean, name, groupe, sousGroupe, attributes };
}
