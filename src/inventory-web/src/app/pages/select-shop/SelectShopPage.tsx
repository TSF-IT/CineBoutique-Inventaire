import { clsx } from "clsx";
import {
  startTransition,
  useCallback,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
} from "react";
import { useLocation, useNavigate } from "react-router-dom";

import {
  buildEntityCards,
  type EntityCardModel,
  type EntityId,
} from "./entities";

import { fetchShops } from "@/api/shops";
import { LoadingIndicator } from "@/app/components/LoadingIndicator";
import { Page } from "@/app/components/Page";
import { Button } from "@/app/components/ui/Button";
import { useInventory } from "@/app/contexts/InventoryContext";
import { clearSelectedUserForShop } from "@/lib/selectedUserStorage";
import { useShop } from "@/state/ShopContext";
import type { Shop } from "@/types/shop";

const DEFAULT_ERROR_MESSAGE = "Impossible de charger les boutiques.";
const INVALID_GUID_ERROR_MESSAGE =
  "Identifiant de boutique invalide. Vérifie le code et réessaie.";
const GUID_REGEX = /^[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}$/i;

const isValidGuid = (value: string) => GUID_REGEX.test(value);

type LoadingState = "idle" | "loading" | "error";

type RedirectState = {
  redirectTo?: string;
} | null;

const ENTITY_TILE_BASE_CLASSES = clsx(
  "tile entity-card flex h-full w-full flex-col items-start justify-between gap-3 px-4 py-3 pr-5 text-left text-base transition will-change-transform",
  "hover:-translate-y-[1px] hover:shadow-elev-1 focus-visible:outline-none",
  "disabled:cursor-not-allowed disabled:opacity-60 disabled:shadow-none disabled:hover:translate-y-0 disabled:hover:shadow-none"
);
const ENTITY_TILE_IDLE_CLASSES = "entity-card--idle";
const ENTITY_TILE_SELECTED_CLASSES = "entity-card--selected";

export const SelectShopPage = () => {
  const { shop, setShop } = useShop();
  const shopDisplayName = shop?.name?.trim() ?? "CinéBoutique";
  const { reset } = useInventory();
  const navigate = useNavigate();
  const location = useLocation();
  const [shops, setShops] = useState<Shop[]>([]);
  const [status, setStatus] = useState<LoadingState>("loading");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [retryCount, setRetryCount] = useState<number>(0);
  const [selectedShopId, setSelectedShopId] = useState(() => shop?.id ?? "");
  const [selectedEntityId, setSelectedEntityId] = useState<EntityId | null>(
    null
  );
  const [selectionError, setSelectionError] = useState<string | null>(null);
  const [isRedirecting, setIsRedirecting] = useState(false);
  const cardsLabelId = useId();
  const cardRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const shopListSectionRef = useRef<HTMLElement | null>(null);
  const pendingScrollToListRef = useRef(false);

  const entityCards = useMemo(() => buildEntityCards(shops), [shops]);

  const entityByShopId = useMemo(() => {
    const map = new Map<string, EntityCardModel>();
    for (const card of entityCards) {
      for (const candidate of card.matches) {
        map.set(candidate.id, card);
      }
    }
    return map;
  }, [entityCards]);

  const redirectState = location.state as RedirectState;
  const redirectTo = useMemo(() => {
    const target = redirectState?.redirectTo;
    if (typeof target !== "string") {
      return null;
    }
    const normalized = target.trim();
    return normalized.length > 0 ? normalized : null;
  }, [redirectState]);

  useEffect(() => {
    const ac = new AbortController();
    let disposed = false;

    const run = async () => {
      try {
        setStatus("loading");
        setErrorMessage(null);

        const list = await fetchShops({
          signal: ac.signal,
        });
        if (disposed) {
          return;
        }

        setShops(list);
        setStatus("idle");
      } catch (e: unknown) {
        if (disposed) {
          return;
        }

        if (
          (e instanceof DOMException && e.name === "AbortError") ||
          (e instanceof Error && e.name === "AbortError")
        ) {
          return;
        }

        let msg = "";
        if (e instanceof Error) msg = e.message;
        else if (typeof e === "string") msg = e;
        else if (typeof e === "object" && e !== null && "message" in e)
          msg = String((e as { message?: string }).message ?? "");

        if (msg === "ABORTED" || msg.toLowerCase().includes("aborted")) {
          return;
        }

        setErrorMessage(msg || "Erreur de chargement");
        setStatus("error");
      }
    };

    run();
    return () => {
      disposed = true;
      ac.abort("route-change");
    };
  }, [retryCount]);
  useEffect(() => {
    if (!shop) {
      startTransition(() => {
        setSelectedShopId("");
        setSelectedEntityId((current) => {
          if (!current) {
            return null;
          }

          const entity = entityCards.find(
            (card) => card.definition.id === current
          );
          if (!entity || entity.matches.length === 0) {
            return null;
          }
          return current;
        });
      });
      return;
    }

    const entity = entityByShopId.get(shop.id) ?? null;
    startTransition(() => {
      setSelectedShopId(shop.id);
      setSelectedEntityId(entity?.definition.id ?? null);
    });
  }, [entityByShopId, entityCards, shop]);

  useEffect(() => {
    if (entityCards.length === 0) {
      startTransition(() => {
        setSelectedEntityId(null);
        setSelectedShopId("");
      });
      return;
    }

    const currentSelection = selectedEntityId
      ? entityCards.find((card) => card.definition.id === selectedEntityId)
      : null;

    if (currentSelection && currentSelection.matches.length === 0) {
      startTransition(() => {
        setSelectedEntityId(null);
        setSelectedShopId("");
      });
      return;
    }

    if (selectedEntityId) {
      return;
    }

    const fallback = entityCards.find((card) => card.matches.length > 0);
    if (!fallback) {
      return;
    }

    startTransition(() => {
      setSelectedEntityId(fallback.definition.id);
      setSelectedShopId((current) => {
        if (
          current &&
          fallback.matches.some((shopOption) => shopOption.id === current)
        ) {
          return current;
        }

        if (
          shop &&
          fallback.matches.some((shopOption) => shopOption.id === shop.id)
        ) {
          return shop.id;
        }

        return "";
      });
    });
  }, [entityCards, selectedEntityId, shop]);

  useEffect(() => {
    startTransition(() => {
      if (!shop) {
        setSelectedShopId("");
        setSelectedEntityId(null);
        return;
      }

      const entity = entityByShopId.get(shop.id) ?? null;
      setSelectedShopId(shop.id);
      setSelectedEntityId(entity?.definition.id ?? null);
    });
  }, [entityByShopId, shop]);

  useEffect(() => {
    if (!selectedShopId) {
      if (selectionError === INVALID_GUID_ERROR_MESSAGE) {
        startTransition(() => {
          setSelectionError(null);
        });
      }
      return;
    }

    if (!isValidGuid(selectedShopId)) {
      if (selectionError !== INVALID_GUID_ERROR_MESSAGE) {
        startTransition(() => {
          setSelectionError(INVALID_GUID_ERROR_MESSAGE);
        });
      }
      return;
    }

    if (selectionError === INVALID_GUID_ERROR_MESSAGE) {
      startTransition(() => {
        setSelectionError(null);
      });
    }
  }, [selectedShopId, selectionError]);

  const focusFirstAvailableCard = useCallback(() => {
    for (let index = 0; index < entityCards.length; index += 1) {
      const card = entityCards[index];
      if (card.matches.length === 0) continue;
      const element = cardRefs.current[index];
      if (element) {
        element.focus();
        return;
      }
    }
  }, [entityCards]);

  const handleRetry = useCallback(() => {
    setRetryCount((count) => count + 1);
    setSelectionError(null);
  }, []);

  const continueWithShop = useCallback(
    (shopToActivate: Shop | null) => {
      if (isRedirecting) {
        return;
      }

      if (!shopToActivate) {
        setSelectionError("Sélectionne une boutique pour continuer.");
        focusFirstAvailableCard();
        return;
      }

      if (!isValidGuid(shopToActivate.id)) {
        setSelectionError(INVALID_GUID_ERROR_MESSAGE);
        focusFirstAvailableCard();
        return;
      }

      if (!shop || shop.id !== shopToActivate.id) {
        reset();
        clearSelectedUserForShop(shopToActivate.id);
      }

      setSelectionError(null);
      setIsRedirecting(true);
      setShop(shopToActivate);

      const navigationOptions = redirectTo
        ? { state: { redirectTo } }
        : undefined;
      navigate("/select-user", navigationOptions);
    },
    [
      focusFirstAvailableCard,
      isRedirecting,
      navigate,
      redirectTo,
      reset,
      shop,
      setShop,
    ]
  );

  const handleEntitySelection = useCallback(
    (entity: EntityCardModel) => {
      if (entity.matches.length === 0) {
        setSelectionError("Cette entité n’est pas encore disponible.");
        focusFirstAvailableCard();
        return;
      }

      pendingScrollToListRef.current = true;
      setSelectionError(null);
      startTransition(() => {
        setSelectedEntityId(entity.definition.id);
        setSelectedShopId((current) => {
          if (
            current &&
            entity.matches.some((shopOption) => shopOption.id === current)
          ) {
            return current;
          }

          if (
            shop &&
            entity.matches.some((shopOption) => shopOption.id === shop.id)
          ) {
            return shop.id;
          }

          return "";
        });
      });
    },
    [focusFirstAvailableCard, shop]
  );

  const isLoadingShops = status === "loading";
  const shouldShowShopError = status === "error" && !isRedirecting;
  const shouldShowShopForm = status === "idle" && !isRedirecting;
  const selectedEntity = useMemo(
    () =>
      entityCards.find((card) => card.definition.id === selectedEntityId) ??
      null,
    [entityCards, selectedEntityId]
  );
  const allEntitiesUnavailable =
    entityCards.length > 0 &&
    entityCards.every((card) => card.matches.length === 0);

  const handleShopSelection = useCallback(
    (shopToActivate: Shop) => {
      setSelectedShopId(shopToActivate.id);
      continueWithShop(shopToActivate);
    },
    [continueWithShop]
  );

  useEffect(() => {
    if (!pendingScrollToListRef.current) {
      return;
    }
    if (!selectedEntityId) {
      pendingScrollToListRef.current = false;
      return;
    }
    const section = shopListSectionRef.current;
    if (!section) {
      pendingScrollToListRef.current = false;
      return;
    }
    if (typeof window === "undefined") {
      pendingScrollToListRef.current = false;
      return;
    }

    const mediaQuery = window.matchMedia?.("(max-width: 640px)");
    const isCompactViewport = mediaQuery
      ? mediaQuery.matches
      : window.innerWidth <= 640;
    if (!isCompactViewport) {
      pendingScrollToListRef.current = false;
      return;
    }

    pendingScrollToListRef.current = false;
    window.requestAnimationFrame(() => {
      const { top } = section.getBoundingClientRect();
      const offset = Math.max(0, window.scrollY + top - 16);
      window.scrollTo({ top: offset, behavior: "smooth" });
    });
  }, [selectedEntityId]);

  return (
    <Page className="px-4 py-6 sm:px-6">
      <main className="flex flex-1 flex-col gap-8">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.4em] text-brand-500/90 dark:text-brand-200/90">
            {shopDisplayName}
          </p>
          <h1 className="mt-2 text-3xl font-semibold tracking-tight text-(--cb-text)">
            Choisir une entité
          </h1>
          <p className="mt-2 max-w-xl text-sm leading-relaxed text-(--cb-muted)">
            Sélectionnez votre entité pour poursuivre l’identification.
          </p>
        </div>

        {isLoadingShops && (
          <LoadingIndicator label="Chargement des boutiques…" />
        )}

        {shouldShowShopError && (
          <div
            className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-200"
            role="alert"
          >
            <p className="font-semibold">
              {errorMessage ?? DEFAULT_ERROR_MESSAGE}
            </p>
            <p className="mt-1">Vérifiez votre connexion puis réessayez.</p>
            <Button className="mt-4" variant="secondary" onClick={handleRetry}>
              Réessayer
            </Button>
          </div>
        )}

        {isRedirecting && (
          <div className="card card--elev1 p-6 text-center">
            <LoadingIndicator label="Redirection en cours…" />
            <p className="mt-4 text-sm text-(--cb-muted) dark:text-(--cb-muted)">
              Merci de patienter pendant la redirection vers l’identification.
            </p>
          </div>
        )}

        {shouldShowShopForm && (
          <>
            <form
              className="flex flex-col gap-5"
              onSubmit={(event) => event.preventDefault()}
            >
              <fieldset className="flex flex-col gap-4 border-0 p-0">
                <legend id={cardsLabelId} className="sr-only">
                  Boutiques disponibles
                </legend>
                <div
                  aria-labelledby={cardsLabelId}
                  className="flex flex-col gap-3 sm:grid sm:grid-cols-2 sm:gap-3"
                  role="radiogroup"
                >
                  {entityCards.map((card, index) => {
                    const isSelected = card.definition.id === selectedEntityId;
                    const isDisabled = card.matches.length === 0;

                    return (
                      <button
                        key={card.definition.id}
                        ref={(element) => {
                          cardRefs.current[index] = element;
                        }}
                        type="button"
                        role="radio"
                        aria-checked={isSelected}
                        aria-disabled={isDisabled}
                        aria-label={
                          isDisabled
                            ? `${card.definition.label} indisponible pour le moment`
                            : card.definition.label
                        }
                        disabled={isDisabled}
                        onClick={() => handleEntitySelection(card)}
                        data-state={isSelected ? "selected" : "idle"}
                        className={clsx(
                          ENTITY_TILE_BASE_CLASSES,
                          isSelected
                            ? ENTITY_TILE_SELECTED_CLASSES
                            : ENTITY_TILE_IDLE_CLASSES,
                          isDisabled && "entity-card--disabled"
                        )}
                      >
                        <span
                          className="entity-card__indicator"
                          aria-hidden="true"
                        >
                          <svg
                            className="h-3.5 w-3.5"
                            viewBox="0 0 20 20"
                            fill="none"
                            xmlns="http://www.w3.org/2000/svg"
                          >
                            <path
                              d="M16.707 6.293a1 1 0 0 0-1.414 0L8.5 13.086l-2.793-2.793a1 1 0 0 0-1.414 1.414l3.5 3.5a1 1 0 0 0 1.414 0l7-7a1 1 0 0 0 0-1.414Z"
                              fill="currentColor"
                            />
                          </svg>
                        </span>
                        <span className="text-lg font-semibold">
                          {card.definition.label}
                        </span>
                        <span className="text-sm text-(--cb-muted) dark:text-(--cb-muted)">
                          {card.definition.description}
                        </span>
                        <span
                          className={clsx(
                            "inline-flex items-center rounded-full border px-3 py-1 text-xs font-medium uppercase tracking-wide transition-colors",
                            isDisabled
                              ? "border-(--cb-border-soft) bg-(--cb-surface-soft) text-(--cb-muted) opacity-70"
                              : isSelected
                              ? "border-brand-500/40 bg-brand-500/15 text-brand-700 shadow-panel-soft dark:border-brand-400/40 dark:bg-brand-400/20 dark:text-brand-100"
                              : "border-(--cb-border-soft) bg-(--cb-surface-soft) text-(--cb-muted)"
                          )}
                        >
                          {isDisabled
                            ? "Bientôt disponible"
                            : card.matches.length === 1
                            ? "1 boutique disponible"
                            : `${card.matches.length} boutiques disponibles`}
                        </span>
                      </button>
                    );
                  })}
                </div>
              </fieldset>
              {selectedEntity && (
                <section
                  ref={shopListSectionRef}
                  aria-label={selectedEntity.definition.label}
                  className="space-y-3 rounded-3xl border border-(--cb-border-soft) bg-(--cb-surface) p-4 shadow-panel-soft backdrop-blur-sm"
                >
                  {selectedEntity.matches.length > 0 ? (
                    <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4">
                      {selectedEntity.matches.map((shopOption) => {
                        const isActive = shopOption.id === selectedShopId;
                        return (
                          <li key={shopOption.id} className="h-full">
                            <button
                              type="button"
                              data-testid={`shop-${shopOption.id}`}
                              data-shop-id={shopOption.id}
                              onClick={() => handleShopSelection(shopOption)}
                              className={clsx(
                                "tile focus-ring flex h-full w-full items-center justify-between rounded-2xl border border-(--cb-border-soft) bg-(--cb-surface-soft) px-4 py-3 text-left text-sm font-medium transition hover:-translate-y-0.5 hover:shadow-panel-soft",
                                isActive
                                  ? "border-brand-500/60 bg-brand-500/15 text-brand-800 shadow-panel-soft dark:border-brand-400/60 dark:bg-brand-400/20 dark:text-brand-100"
                                  : "text-(--cb-text) hover:border-brand-400/60 hover:bg-brand-500/10 dark:border-(--cb-border-soft) dark:text-(--cb-text) dark:hover:border-brand-400/60 dark:hover:bg-brand-400/15"
                              )}
                            >
                              <span className="flex-1 truncate">
                                {shopOption.name}
                              </span>
                              {isActive && (
                                <span className="ml-3 inline-flex items-center rounded-full bg-brand-500 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-white shadow-sm dark:bg-brand-400 dark:text-(--cb-text)">
                                  Sélectionné
                                </span>
                              )}
                            </button>
                          </li>
                        );
                      })}
                    </ul>
                  ) : (
                    <p className="text-sm text-(--cb-muted) dark:text-(--cb-muted)">
                      Aucune boutique n’est disponible pour le moment.
                    </p>
                  )}
                </section>
              )}
              {allEntitiesUnavailable && (
                <p
                  id="shop-help"
                  className="text-sm text-(--cb-muted) dark:text-(--cb-muted)"
                >
                  Aucune entité n’est disponible pour le moment.
                </p>
              )}
              {selectionError && (
                <p
                  className="text-sm text-red-600 dark:text-red-400"
                  role="alert"
                >
                  {selectionError}
                </p>
              )}
            </form>
          </>
        )}
      </main>
    </Page>
  );
};

export default SelectShopPage;
