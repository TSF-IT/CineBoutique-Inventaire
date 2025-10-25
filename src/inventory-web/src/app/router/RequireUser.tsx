import { useEffect, useMemo, useReducer, useRef } from "react";
import type { ReactElement } from "react";
import { Navigate, Outlet, useLocation } from "react-router-dom";

import { fetchShopUsers } from "@/app/api/shopUsers";
import { LoadingIndicator } from "@/app/components/LoadingIndicator";
import { useInventory } from "@/app/contexts/InventoryContext";
import {
  clearSelectedUserForShop,
  loadSelectedUserForShop,
  saveSelectedUserForShop,
  toShopUserFromSnapshot,
} from "@/lib/selectedUserStorage";
import { useShop } from "@/state/ShopContext";

type GuardState =
  | { status: "checking" }
  | { status: "loading" }
  | { status: "ready" }
  | { status: "redirect"; target: "select-user" | "select-shop" };

type GuardAction =
  | { type: "checking" }
  | { type: "loading" }
  | { type: "ready" }
  | { type: "redirect"; target: "select-user" | "select-shop" };

const guardReducer = (_state: GuardState, action: GuardAction): GuardState => {
  switch (action.type) {
    case "checking":
      return { status: "checking" };
    case "loading":
      return { status: "loading" };
    case "ready":
      return { status: "ready" };
    case "redirect":
      return { status: "redirect", target: action.target };
    default:
      return _state;
  }
};

const extractStoredUserId = (
  stored: ReturnType<typeof loadSelectedUserForShop>
): string | null => {
  if (!stored) {
    return null;
  }

  if ("userId" in stored && typeof stored.userId === "string") {
    const candidate = stored.userId.trim();
    return candidate.length > 0 ? candidate : null;
  }

  if ("id" in stored && typeof stored.id === "string") {
    const candidate = stored.id.trim();
    return candidate.length > 0 ? candidate : null;
  }

  return null;
};

export default function RequireUser(): ReactElement | null {
  const { shop, isLoaded } = useShop();
  const { selectedUser, setSelectedUser } = useInventory();
  const loc = useLocation();
  const [state, dispatch] = useReducer(guardReducer, { status: "checking" });
  const resolvedUserIdRef = useRef<string | null>(null);
  const lastShopIdRef = useRef<string | null>(null);
  const shopId = shop?.id?.trim() || null;
  const selectedUserId = selectedUser?.id ?? null;

  useEffect(() => {
    if (lastShopIdRef.current !== shopId) {
      resolvedUserIdRef.current = null;
      lastShopIdRef.current = shopId;
    }
  }, [shopId]);

  const redirectTarget = useMemo(
    () => `${loc.pathname}${loc.search}${loc.hash}`,
    [loc.hash, loc.pathname, loc.search]
  );

  useEffect(() => {
    if (!isLoaded) {
      dispatch({ type: "checking" });
      return;
    }

    if (!shopId) {
      dispatch({ type: "redirect", target: "select-shop" });
      return;
    }

    const stored = loadSelectedUserForShop(shopId);
    const storedUserId = extractStoredUserId(stored);

    if (!storedUserId) {
      resolvedUserIdRef.current = null;
      dispatch({ type: "redirect", target: "select-user" });
      return;
    }

    if (resolvedUserIdRef.current === storedUserId) {
      dispatch({ type: "ready" });
      return;
    }

    if (selectedUserId && selectedUserId === storedUserId) {
      resolvedUserIdRef.current = storedUserId;
      dispatch({ type: "ready" });
      return;
    }

    const snapshotUser = toShopUserFromSnapshot(stored, shopId);
    if (snapshotUser) {
      resolvedUserIdRef.current = storedUserId;
      setSelectedUser(snapshotUser);
      dispatch({ type: "ready" });
      return;
    }

    let cancelled = false;
    dispatch({ type: "loading" });
    (async () => {
      try {
        const users = await fetchShopUsers(shopId);
        if (cancelled) {
          return;
        }

        const found = users.find((user) => user.id === storedUserId) ?? null;
        if (!found) {
          clearSelectedUserForShop(shopId);
          resolvedUserIdRef.current = null;
          dispatch({ type: "redirect", target: "select-user" });
          return;
        }

        resolvedUserIdRef.current = storedUserId;
        setSelectedUser(found);
        saveSelectedUserForShop(shopId, found);
        dispatch({ type: "ready" });
      } catch (error) {
        if (cancelled) {
          return;
        }

        const shopNotFound = Boolean(
          (error as { __shopNotFound?: boolean } | null)?.__shopNotFound
        );
        if (shopNotFound) {
          resolvedUserIdRef.current = null;
          dispatch({ type: "redirect", target: "select-shop" });
          return;
        }

        clearSelectedUserForShop(shopId);
        resolvedUserIdRef.current = null;
        dispatch({ type: "redirect", target: "select-user" });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [isLoaded, selectedUser, selectedUserId, setSelectedUser, shopId]);

  if (!isLoaded) {
    return null;
  }

  if (state.status === "redirect") {
    if (state.target === "select-shop") {
      return <Navigate to="/select-shop" state={{ from: loc }} replace />;
    }

    return (
      <Navigate
        to="/select-user"
        state={{ from: loc, redirectTo: redirectTarget }}
        replace
      />
    );
  }

  if (state.status === "loading" || state.status === "checking") {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-50 px-4 py-10 dark:bg-slate-950">
        <LoadingIndicator label="Chargement de votre session utilisateur…" />
      </div>
    );
  }

  return <Outlet />;
}
