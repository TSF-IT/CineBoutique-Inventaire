import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
} from "react";
import { useNavigate } from "react-router-dom";

import { fetchProductByEan, startInventoryRun } from "../../api/inventoryApi";
import { BarcodeScanner } from "../../components/BarcodeScanner";
import { ScannedRow } from "../../components/inventory/ScannedRow";
import { useInventory } from "../../contexts/InventoryContext";
import type { Product } from "../../types/inventory";

import { useCamera, type CameraOptions } from "@/hooks/useCamera";
import { useScanRejectionFeedback } from "@/hooks/useScanRejectionFeedback";
import { RequestTimeoutError, type HttpError } from "@/lib/api/http";
import { useShop } from "@/state/ShopContext";

const MAX_SCAN_LENGTH = 32;
const LOCK_RELEASE_DELAY = 700;

const sanitizeScanValue = (value: string) => value.replace(/\r|\n/g, "");

const isScanLengthValid = (code: string) =>
  code.length > 0 && code.length <= MAX_SCAN_LENGTH;

const formatCameraError = (error: unknown) => {
  if (error instanceof Error) {
    return error.message || error.name;
  }
  if (
    error &&
    typeof error === "object" &&
    "name" in error &&
    typeof (error as { name?: unknown }).name === "string"
  ) {
    const { name } = error as { name?: string };
    if (name) {
      return name;
    }
  }
  return String(error);
};

export const ScanCameraPage = () => {
  const navigate = useNavigate();
  const { shop } = useShop();
  const {
    selectedUser,
    location,
    countType,
    items,
    addOrIncrementItem,
    setQuantity,
    removeItem,
    sessionId,
    setSessionId,
  } = useInventory();
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [highlightEan, setHighlightEan] = useState<string | null>(null);
  const highlightTimeoutRef = useRef<number | null>(null);
  const statusTimeoutRef = useRef<number | null>(null);
  const lockTimeoutRef = useRef<number | null>(null);
  const lockedEanRef = useRef<string | null>(null);
  const resumeCameraOnVisibleRef = useRef(false);
  const lastVisibilityStateRef = useRef<string | null>(
    typeof document !== "undefined" ? document.visibilityState : null
  );
  const scheduleMeasureRef = useRef<(() => void) | null>(null);
  const [matchMediaLandscape, setMatchMediaLandscape] = useState<boolean>(
    () => {
      if (
        typeof window === "undefined" ||
        typeof window.matchMedia !== "function"
      ) {
        return false;
      }
      return window.matchMedia("(orientation: landscape)").matches;
    }
  );
  const [viewportSize, setViewportSize] = useState<{
    width: number;
    height: number;
  } | null>(null);
  const triggerScanRejectionFeedback = useScanRejectionFeedback();
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const cameraOptions = useMemo<CameraOptions>(
    () => ({
      autoResumeOnVisible: false,
      constraints: {
        video: { facingMode: { ideal: "environment" } },
        audio: false,
      },
    }),
    []
  );

  const {
    active: cameraActive,
    error: cameraError,
    stop: stopCamera,
    start: startCamera,
  } = useCamera(videoRef, cameraOptions);

  const shopName = shop?.name ?? "Boutique";
  const ownerUserId = selectedUser?.id?.trim() ?? "";
  const locationId = location?.id?.trim() ?? "";
  const countTypeValue = typeof countType === "number" ? countType : null;
  const sessionRunId = typeof sessionId === "string" ? sessionId.trim() : "";
  const [hasCameraBooted, setHasCameraBooted] = useState(false);

  const commitViewportSize = useCallback(() => {
    if (typeof window === "undefined") {
      return;
    }
    const visualViewport = window.visualViewport;
    const nextWidth =
      visualViewport?.width ??
      window.innerWidth ??
      document.documentElement?.clientWidth ??
      0;
    const nextHeight =
      visualViewport?.height ??
      window.innerHeight ??
      document.documentElement?.clientHeight ??
      0;

    if (!nextWidth || !nextHeight) {
      return;
    }

    const rounded = {
      width: Math.round(nextWidth),
      height: Math.round(nextHeight),
    };

    setViewportSize((previous) => {
      if (!previous) {
        return rounded;
      }
      if (
        Math.abs(previous.width - rounded.width) <= 1 &&
        Math.abs(previous.height - rounded.height) <= 1
      ) {
        return previous;
      }
      return rounded;
    });

    if (typeof window.matchMedia !== "function") {
      setMatchMediaLandscape(rounded.width > rounded.height);
    }
  }, []);

  useEffect(() => {
    if (typeof window === "undefined") {
      return;
    }

    let rafId = 0;
    const schedule = () => {
      if (rafId) {
        cancelAnimationFrame(rafId);
      }
      rafId = requestAnimationFrame(() => {
        rafId = 0;
        commitViewportSize();
      });
    };

    scheduleMeasureRef.current = schedule;
    schedule();
    const delayed = window.setTimeout(schedule, 160);

    window.addEventListener("resize", schedule);
    window.addEventListener("orientationchange", schedule);
    window.addEventListener("pageshow", schedule);
    const visualViewport = window.visualViewport;
    visualViewport?.addEventListener("resize", schedule);
    visualViewport?.addEventListener("scroll", schedule);

    return () => {
      scheduleMeasureRef.current = null;
      if (rafId) {
        cancelAnimationFrame(rafId);
      }
      window.clearTimeout(delayed);
      window.removeEventListener("resize", schedule);
      window.removeEventListener("orientationchange", schedule);
      window.removeEventListener("pageshow", schedule);
      visualViewport?.removeEventListener("resize", schedule);
      visualViewport?.removeEventListener("scroll", schedule);
    };
  }, [commitViewportSize]);

  useEffect(() => {
    if (
      typeof window === "undefined" ||
      typeof window.matchMedia !== "function"
    ) {
      return;
    }
    const query = window.matchMedia("(orientation: landscape)");
    const handleChange = (event: MediaQueryListEvent) => {
      setMatchMediaLandscape(event.matches);
      commitViewportSize();
    };
    const handleLegacy = (event: MediaQueryListEvent) => {
      handleChange(event);
    };
    setMatchMediaLandscape(query.matches);
    commitViewportSize();
    if (typeof query.addEventListener === "function") {
      query.addEventListener("change", handleChange);
      return () => {
        query.removeEventListener("change", handleChange);
      };
    }
    query.addListener(handleLegacy);
    return () => {
      query.removeListener(handleLegacy);
    };
  }, [commitViewportSize]);

  useEffect(() => {
    if (typeof document === "undefined") {
      return;
    }
    const { body, documentElement } = document;
    const previousOverflow = body.style.overflow;
    const previousOverscroll = documentElement.style.overscrollBehaviorY;

    body.style.overflow = "hidden";
    documentElement.style.overscrollBehaviorY = "contain";

    return () => {
      body.style.overflow = previousOverflow;
      documentElement.style.overscrollBehaviorY = previousOverscroll;
    };
  }, []);

  useEffect(() => {
    if (cameraActive) {
      setHasCameraBooted(true);
    }
  }, [cameraActive]);

  useEffect(() => {
    if (cameraError) {
      setHasCameraBooted(false);
    }
  }, [cameraError]);

  useEffect(() => {
    if (typeof document === "undefined") {
      return;
    }
    lastVisibilityStateRef.current = document.visibilityState;
    const handleVisibility = () => {
      const nextState = document.visibilityState;
      if (lastVisibilityStateRef.current === nextState) {
        return;
      }
      lastVisibilityStateRef.current = nextState;
      if (nextState === "hidden") {
        resumeCameraOnVisibleRef.current = cameraActive;
        stopCamera();
      } else if (resumeCameraOnVisibleRef.current) {
        resumeCameraOnVisibleRef.current = false;
        void startCamera();
        scheduleMeasureRef.current?.();
      }
    };
    document.addEventListener("visibilitychange", handleVisibility);
    return () => {
      document.removeEventListener("visibilitychange", handleVisibility);
    };
  }, [cameraActive, startCamera, stopCamera]);

  useEffect(() => {
    if (typeof window === "undefined") {
      return;
    }
    const handleLifecycle = () => {
      resumeCameraOnVisibleRef.current = false;
      stopCamera();
    };
    window.addEventListener("pagehide", handleLifecycle);
    window.addEventListener("beforeunload", handleLifecycle);
    return () => {
      window.removeEventListener("pagehide", handleLifecycle);
      window.removeEventListener("beforeunload", handleLifecycle);
    };
  }, [stopCamera]);

  const viewportHeight = viewportSize?.height ?? null;
  const viewportWidth = viewportSize?.width ?? null;
  const isLandscape = useMemo(() => {
    if (viewportHeight !== null && viewportWidth !== null) {
      if (viewportWidth === viewportHeight) {
        return matchMediaLandscape;
      }
      return viewportWidth > viewportHeight;
    }
    return matchMediaLandscape;
  }, [matchMediaLandscape, viewportHeight, viewportWidth]);

  const cameraSectionPixels = useMemo(() => {
    if (!viewportHeight) {
      return null;
    }
    const minHeight = isLandscape ? 220 : 320;
    const reserve = isLandscape ? 180 : 240;
    const maxHeight = Math.max(viewportHeight - reserve, minHeight);
    const target = Math.round(viewportHeight * (isLandscape ? 0.5 : 0.58));
    return Math.min(Math.max(target, minHeight), maxHeight);
  }, [isLandscape, viewportHeight]);

  const viewportStyle = useMemo<CSSProperties | undefined>(() => {
    if (!viewportHeight) {
      return undefined;
    }
    return {
      minHeight: viewportHeight,
      height: viewportHeight,
    };
  }, [viewportHeight]);

  const cameraSectionStyle = useMemo<CSSProperties>(() => {
    if (!cameraSectionPixels) {
      return {
        minHeight: isLandscape ? "50vh" : "58vh",
      };
    }
    return {
      height: cameraSectionPixels,
      minHeight: cameraSectionPixels,
    };
  }, [cameraSectionPixels, isLandscape]);

  const bottomSectionPaddingStyle = useMemo<CSSProperties>(
    () => ({
      paddingBottom: "calc(env(safe-area-inset-bottom, 0px) + 1.5rem)",
    }),
    []
  );

  const bottomSectionStyle = useMemo<CSSProperties>(() => {
    if (!viewportHeight || !cameraSectionPixels) {
      return bottomSectionPaddingStyle;
    }
    const minimum = isLandscape ? 200 : 260;
    const available = Math.max(viewportHeight - cameraSectionPixels, minimum);
    return {
      ...bottomSectionPaddingStyle,
      height: available,
      minHeight: available,
      maxHeight: available,
    };
  }, [
    bottomSectionPaddingStyle,
    cameraSectionPixels,
    isLandscape,
    viewportHeight,
  ]);

  const topOverlayStyle = useMemo<CSSProperties>(
    () => ({
      paddingTop: `calc(env(safe-area-inset-top, 0px) + ${
        isLandscape ? 0.75 : 1.1
      }rem)`,
    }),
    [isLandscape]
  );

  useEffect(() => {
    if (!selectedUser) {
      navigate("/select-shop", { replace: true });
      return;
    }
    if (!locationId) {
      navigate("/inventory/location", { replace: true });
      return;
    }
    if (!countTypeValue) {
      navigate("/inventory/count-type", { replace: true });
    }
  }, [countTypeValue, locationId, navigate, selectedUser]);

  useEffect(() => {
    return () => {
      resumeCameraOnVisibleRef.current = false;
      stopCamera();
      if (highlightTimeoutRef.current) {
        window.clearTimeout(highlightTimeoutRef.current);
        highlightTimeoutRef.current = null;
      }
      if (statusTimeoutRef.current) {
        window.clearTimeout(statusTimeoutRef.current);
        statusTimeoutRef.current = null;
      }
      if (lockTimeoutRef.current) {
        window.clearTimeout(lockTimeoutRef.current);
        lockTimeoutRef.current = null;
      }
      lockedEanRef.current = null;
    };
  }, [stopCamera]);

  useEffect(() => {
    if (!highlightEan) {
      return;
    }
    if (highlightTimeoutRef.current) {
      window.clearTimeout(highlightTimeoutRef.current);
    }
    highlightTimeoutRef.current = window.setTimeout(() => {
      setHighlightEan(null);
      highlightTimeoutRef.current = null;
    }, 700);
  }, [highlightEan]);

  useEffect(() => {
    if (!statusMessage) {
      return;
    }
    if (statusTimeoutRef.current) {
      window.clearTimeout(statusTimeoutRef.current);
    }
    statusTimeoutRef.current = window.setTimeout(() => {
      setStatusMessage(null);
      statusTimeoutRef.current = null;
    }, 2200);
  }, [statusMessage]);

  const totalQuantity = useMemo(
    () => items.reduce((acc, item) => acc + item.quantity, 0),
    [items]
  );

  const orderedItems = useMemo(() => [...items].reverse(), [items]);

  const cameraErrorLabel = useMemo(
    () => (cameraError ? formatCameraError(cameraError) : null),
    [cameraError]
  );

  const ensureScanPrerequisites = useCallback(() => {
    if (!shop?.id) {
      throw new Error(
        "Sélectionnez une boutique valide avant de scanner un produit."
      );
    }
    if (!ownerUserId) {
      throw new Error(
        "Sélectionnez un utilisateur avant de scanner un produit."
      );
    }
    if (!locationId) {
      throw new Error("Sélectionnez une zone avant de scanner un produit.");
    }
    if (!countTypeValue) {
      throw new Error(
        "Choisissez un type de comptage avant de scanner un produit."
      );
    }
  }, [countTypeValue, locationId, ownerUserId, shop?.id]);

  const ensureActiveRun = useCallback(async () => {
    if (items.length > 0 && sessionRunId) {
      return sessionRunId;
    }
    if (sessionRunId) {
      return sessionRunId;
    }
    ensureScanPrerequisites();
    const response = await startInventoryRun(locationId, {
      shopId: shop!.id,
      ownerUserId,
      countType: countTypeValue!,
    });
    const nextRunId =
      typeof response.runId === "string" ? response.runId.trim() : "";
    if (nextRunId) {
      setSessionId(nextRunId);
      return nextRunId;
    }
    return null;
  }, [
    countTypeValue,
    ensureScanPrerequisites,
    items.length,
    locationId,
    ownerUserId,
    sessionRunId,
    setSessionId,
    shop,
  ]);

  const addProductToSession = useCallback(
    async (product: Product) => {
      try {
        await ensureActiveRun();
      } catch (error) {
        setStatusMessage(null);
        setErrorMessage(
          error instanceof Error
            ? error.message
            : "Impossible de démarrer le comptage."
        );
        return false;
      }
      addOrIncrementItem(product);
      return true;
    },
    [addOrIncrementItem, ensureActiveRun]
  );

  const armScanLock = useCallback((ean: string | null) => {
    if (lockTimeoutRef.current) {
      window.clearTimeout(lockTimeoutRef.current);
      lockTimeoutRef.current = null;
    }
    lockedEanRef.current = ean;
    if (!ean) {
      return;
    }
    lockTimeoutRef.current = window.setTimeout(() => {
      lockedEanRef.current = null;
      lockTimeoutRef.current = null;
    }, LOCK_RELEASE_DELAY);
  }, []);

  const refreshScanLock = useCallback(() => {
    const current = lockedEanRef.current;
    if (!current) {
      return;
    }
    armScanLock(current);
  }, [armScanLock]);

  const handleProductAdded = useCallback((product: Product) => {
    setStatusMessage(`${product.name} ajouté`);
    const normalizedEan = product.ean?.trim() ?? null;
    if (normalizedEan) {
      setHighlightEan(normalizedEan);
    } else {
      setHighlightEan(null);
    }
  }, []);

  const handleDetected = useCallback(
    async (rawValue: string) => {
      const sanitized = sanitizeScanValue(rawValue);
      if (!sanitized) {
        return;
      }

      if (lockedEanRef.current && lockedEanRef.current === sanitized) {
        refreshScanLock();
        return;
      }

      if (!isScanLengthValid(sanitized)) {
        setErrorMessage(
          `Code ${sanitized} invalide : ${MAX_SCAN_LENGTH} caractères maximum.`
        );
        setStatusMessage(null);
        armScanLock(sanitized);
        return;
      }

      try {
        ensureScanPrerequisites();
      } catch (error) {
        setErrorMessage(
          error instanceof Error
            ? error.message
            : "Impossible de lancer le scan."
        );
        setStatusMessage(null);
        armScanLock(null);
        return;
      }

      setStatusMessage(`Lecture de ${sanitized}…`);
      setErrorMessage(null);
      armScanLock(sanitized);

      try {
        const product = await fetchProductByEan(sanitized);
        const added = await addProductToSession(product);
        if (added) {
          handleProductAdded(product);
        }
      } catch (error) {
        if (error instanceof RequestTimeoutError) {
          setErrorMessage(
            "Lecture du produit trop longue. Vérifiez la connexion et réessayez."
          );
          triggerScanRejectionFeedback();
        } else {
          const err = error as HttpError;
          if (err?.status === 404) {
            setErrorMessage(
              `Code ${sanitized} introuvable dans la liste des produits à inventorier.`
            );
            triggerScanRejectionFeedback();
          } else {
            setErrorMessage("Échec de la récupération du produit. Réessayez.");
          }
        }
        setStatusMessage(null);
      } finally {
        armScanLock(sanitized);
      }
    },
    [
      addProductToSession,
      armScanLock,
      ensureScanPrerequisites,
      handleProductAdded,
      refreshScanLock,
      triggerScanRejectionFeedback,
    ]
  );

  const handleGoBack = useCallback(() => {
    resumeCameraOnVisibleRef.current = false;
    stopCamera();
    navigate("/inventory/session");
  }, [navigate, stopCamera]);

  const handleDec = useCallback(
    (ean: string, quantity: number) => {
      if (quantity <= 1) {
        removeItem(ean);
        return;
      }
      setQuantity(ean, quantity - 1, { promote: false });
    },
    [removeItem, setQuantity]
  );

  const handleInc = useCallback(
    (ean: string, quantity: number) => {
      setQuantity(ean, quantity + 1, { promote: false });
    },
    [setQuantity]
  );

  const handleSetQuantity = useCallback(
    (ean: string, next: number | null) => {
      if (next === null) {
        return;
      }
      if (next <= 0) {
        removeItem(ean);
        return;
      }
      setQuantity(ean, next);
    },
    [removeItem, setQuantity]
  );

  return (
    <div
      className="scan-camera-screen relative flex min-h-dvh h-dvh flex-col overflow-hidden bg-black text-white"
      data-testid="scan-camera-page"
      style={viewportStyle}
    >
      <div
        className="relative flex-none overflow-hidden"
        style={cameraSectionStyle}
      >
        <BarcodeScanner
          active={cameraActive}
          onDetected={handleDetected}
          presentation="immersive"
          enableTorchToggle
          camera={{ videoRef, active: cameraActive, error: cameraError }}
        />
        <div className="pointer-events-none absolute inset-x-0 top-0 z-10 h-28 bg-linear-to-b from-black/80 via-black/40 to-transparent" />
        <div
          className="pointer-events-none absolute inset-x-0 top-0 z-20 flex items-start justify-between px-5 sm:px-8"
          style={topOverlayStyle}
        >
          <button
            type="button"
            className="pointer-events-auto inline-flex items-center gap-2 rounded-full bg-black/60 px-4 py-2 text-sm font-semibold text-white shadow-lg backdrop-blur transition hover:bg-black/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white/70"
            onClick={handleGoBack}
            data-testid="scan-camera-back"
          >
            <span aria-hidden className="text-base">
              ←
            </span>
            Retour
          </button>
        </div>
        <div className="pointer-events-none absolute inset-x-0 bottom-6 z-20 flex justify-center px-6">
          {!cameraActive && !cameraError && !hasCameraBooted && (
            <span className="rounded-full bg-black/70 px-3 py-1 text-sm font-semibold text-white shadow-lg backdrop-blur">
              Démarrage caméra…
            </span>
          )}
          {!cameraActive && !cameraError && hasCameraBooted && (
            <span className="rounded-full bg-black/70 px-3 py-1 text-sm font-semibold text-white/80 shadow-lg backdrop-blur">
              Caméra en pause
            </span>
          )}
          {cameraError && (
            <span className="rounded-full bg-black/70 px-3 py-1 text-sm font-semibold text-rose-200 shadow-lg backdrop-blur">
              Caméra indisponible : {cameraErrorLabel ?? "Erreur inconnue"}
            </span>
          )}
        </div>
      </div>
      <div
        className="flex min-h-0 flex-1 flex-col rounded-t-4xl bg-white text-slate-900 shadow-[0_-20px_48px_-32px_rgba(15,23,42,0.55)] transition-colors duration-300 dark:bg-slate-950 dark:text-white"
        style={bottomSectionStyle}
        data-testid="scan-sheet"
      >
        <div className="flex items-center justify-between gap-4 px-6 pt-6">
          <div className="min-w-0">
            <p className="text-[11px] uppercase tracking-[0.28em] text-slate-400 dark:text-slate-500">
              {shopName}
            </p>
            <p className="mt-2 truncate text-lg font-semibold leading-tight text-slate-900 dark:text-white">
              {location?.label ?? "Zone inconnue"}
            </p>
          </div>
          <span className="shrink-0 rounded-full bg-slate-900/90 px-3 py-1 text-sm font-semibold text-white dark:bg-slate-700/80">
            {totalQuantity} pièce{totalQuantity > 1 ? "s" : ""}
          </span>
        </div>
        <div className="space-y-2 px-6 pt-3">
          {statusMessage && (
            <div className="rounded-2xl bg-emerald-50 px-3 py-2 text-xs font-semibold text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-200">
              {statusMessage}
            </div>
          )}
          {errorMessage && (
            <div className="rounded-2xl bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-600 dark:bg-rose-500/10 dark:text-rose-200">
              {errorMessage}
            </div>
          )}
        </div>
        <div className="flex-1 overflow-y-auto px-6 pb-10 pt-4">
          {orderedItems.length === 0 ? (
            <p className="text-sm text-slate-500 dark:text-slate-400">
              Scannez un article pour commencer le comptage.
            </p>
          ) : (
            <ul className="space-y-3">
              {orderedItems.map((item) => {
                const ean = item.product.ean;
                return (
                  <ScannedRow
                    key={item.id}
                    id={item.id}
                    ean={item.product.ean}
                    label={item.product.name}
                    sku={item.product.sku}
                    subGroup={item.product.subGroup}
                    qty={item.quantity}
                    highlight={highlightEan === item.product.ean}
                    hasConflict={Boolean(item.hasConflict)}
                    onInc={() => handleInc(ean, item.quantity)}
                    onDec={() => handleDec(ean, item.quantity)}
                    onSetQty={(value) => handleSetQuantity(ean, value)}
                  />
                );
              })}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
};

export default ScanCameraPage;
