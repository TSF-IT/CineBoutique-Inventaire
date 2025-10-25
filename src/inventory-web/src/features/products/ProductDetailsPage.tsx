import { useEffect, useReducer } from "react";
import { useParams } from "react-router-dom";
import { extractBadges } from "./attributeBadges";

type Details = {
  sku: string; ean?: string | null; name: string;
  group?: string | null; subGroup?: string | null;
  attributes?: Record<string, unknown> | null;
};

type DetailsState = {
  data: Details | null;
  loading: boolean;
  error: string | null;
};

type DetailsAction =
  | { type: "start" }
  | { type: "success"; data: Details | null }
  | { type: "error"; message: string }
  | { type: "not-found" };

const initialState: DetailsState = {
  data: null,
  loading: true,
  error: null,
};

const detailsReducer = (state: DetailsState, action: DetailsAction): DetailsState => {
  switch (action.type) {
    case "start":
      return { ...state, loading: true, error: null };
    case "success":
      return { data: action.data, loading: false, error: null };
    case "not-found":
      return { data: null, loading: false, error: "Produit introuvable" };
    case "error":
      return { data: null, loading: false, error: action.message };
    default:
      return state;
  }
};

export function ProductDetailsPage() {
  const { sku = "" } = useParams();
  const [state, dispatch] = useReducer(detailsReducer, initialState);

  useEffect(() => {
    const controller = new AbortController();
    let cancelled = false;

    dispatch({ type: "start" });

    (async () => {
      try {
        const response = await fetch(`/api/products/${encodeURIComponent(sku)}/details`, {
          signal: controller.signal,
        });

        if (cancelled) {
          return;
        }

        if (response.status === 404) {
          dispatch({ type: "not-found" });
          return;
        }

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }

        const payload = (await response.json()) as Details | null;
        dispatch({ type: "success", data: payload ?? null });
      } catch (rawError) {
        if (cancelled) {
          return;
        }

        if ((rawError as DOMException)?.name === "AbortError") {
          return;
        }

        const message =
          rawError instanceof Error && rawError.message
            ? rawError.message
            : String(rawError);
        dispatch({ type: "error", message });
      }
    })();

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [sku]);

  const attrs = state.data?.attributes && typeof state.data.attributes === "object" ? state.data.attributes : null;
  const badges = extractBadges(attrs);

  return (
    <section style={{ display:"grid", gap: 12, maxWidth: 900 }}>
      <h2 style={{ margin:0 }}>Produit {state.data?.sku ?? sku}</h2>
      {state.loading && <div>Chargement…</div>}
      {state.error && <div style={{ color:"#b00020" }}>Erreur : {state.error}</div>}
      {!state.loading && !state.error && state.data && (
        <>
          <div><strong>Nom</strong> : {state.data.name}</div>
          <div><strong>EAN</strong> : {state.data.ean ?? "—"}</div>
          <div><strong>Groupe</strong> : {state.data.group ?? "—"}{state.data.subGroup ? ` / ${state.data.subGroup}` : ""}</div>
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
