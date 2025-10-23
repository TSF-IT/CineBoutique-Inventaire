import { useEffect, useMemo, useRef, useState } from "react";
import { Navigate, Outlet, useLocation } from "react-router-dom";
import type { ReactElement } from "react";
import { useShop } from "@/state/ShopContext";
import {
  clearSelectedUserForShop,
  loadSelectedUserForShop,
  saveSelectedUserForShop,
  toShopUserFromSnapshot,
} from "@/lib/selectedUserStorage";
import { useInventory } from "@/app/contexts/InventoryContext";
import { fetchShopUsers } from "@/app/api/shopUsers";
import { LoadingIndicator } from "@/app/components/LoadingIndicator";

type GuardState =
  | { status: "checking" }
  | { status: "loading" }
  | { status: "ready" }
  | { status: "redirect"; target: "select-user" | "select-shop" };

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
  const [state, setState] = useState<GuardState>({ status: "checking" });
  const resolvedUserIdRef = useRef<string | null>(null);
  const lastShopIdRef = useRef<string | null>(null);

  useEffect(() => {
    const currentShopId = shop?.id ?? null;
    if (lastShopIdRef.current !== currentShopId) {
      resolvedUserIdRef.current = null;
      lastShopIdRef.current = currentShopId;
    }
  }, [shop?.id]);

  const redirectTarget = useMemo(
    () => `${loc.pathname}${loc.search}${loc.hash}`,
    [loc.hash, loc.pathname, loc.search]
  );

  useEffect(() => {
    if (!isLoaded) {
      setState({ status: "checking" });
      return;
    }

    if (!shop) {
      setState({ status: "redirect", target: "select-shop" });
      return;
    }

    const shopId = shop.id;
    const stored = loadSelectedUserForShop(shopId);
    const storedUserId = extractStoredUserId(stored);

    if (!storedUserId) {
      resolvedUserIdRef.current = null;
      setState({ status: "redirect", target: "select-user" });
      return;
    }

    if (resolvedUserIdRef.current === storedUserId) {
      setState({ status: "ready" });
      return;
    }

    if (selectedUser && selectedUser.id === storedUserId) {
      resolvedUserIdRef.current = storedUserId;
      setState({ status: "ready" });
      return;
    }

    const snapshotUser = toShopUserFromSnapshot(stored, shopId);
    if (snapshotUser) {
      resolvedUserIdRef.current = storedUserId;
      setSelectedUser(snapshotUser);
      setState({ status: "ready" });
      return;
    }

    let cancelled = false;
    setState({ status: "loading" });
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
          setState({ status: "redirect", target: "select-user" });
          return;
        }

        resolvedUserIdRef.current = storedUserId;
        setSelectedUser(found);
        saveSelectedUserForShop(shopId, found);
        setState({ status: "ready" });
      } catch (error) {
        if (cancelled) {
          return;
        }

        const shopNotFound = Boolean(
          (error as { __shopNotFound?: boolean } | null)?.__shopNotFound
        );
        if (shopNotFound) {
          resolvedUserIdRef.current = null;
          setState({ status: "redirect", target: "select-shop" });
          return;
        }

        clearSelectedUserForShop(shopId);
        resolvedUserIdRef.current = null;
        setState({ status: "redirect", target: "select-user" });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [isLoaded, selectedUser, selectedUser.id, setSelectedUser, shop, shop.id]);

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
