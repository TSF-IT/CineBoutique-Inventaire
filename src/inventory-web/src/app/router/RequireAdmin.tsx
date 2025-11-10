import { useEffect, useMemo, useRef } from "react";
import type { ReactElement } from "react";
import { Outlet, useLocation, useNavigate } from "react-router-dom";

import { useInventory } from "@/app/contexts/InventoryContext";

type AdminRedirectState = {
  adminAccessDenied: true;
  from?: string | null;
};

export default function RequireAdmin(): ReactElement | null {
  const { selectedUser } = useInventory();
  const navigate = useNavigate();
  const location = useLocation();
  const hasAdminRole = Boolean(selectedUser?.isAdmin);
  const redirectTarget = useMemo(
    () => `${location.pathname}${location.search}${location.hash}`,
    [location.hash, location.pathname, location.search]
  );
  const redirectedRef = useRef(false);

  useEffect(() => {
    if (!selectedUser) {
      return;
    }

    if (hasAdminRole || redirectedRef.current) {
      return;
    }

    redirectedRef.current = true;
    const state: AdminRedirectState = {
      adminAccessDenied: true,
      from: redirectTarget || undefined,
    };
    navigate("/", { replace: true, state });
  }, [hasAdminRole, navigate, redirectTarget, selectedUser]);

  if (!selectedUser) {
    return null;
  }

  if (!hasAdminRole) {
    return null;
  }

  return <Outlet />;
}
