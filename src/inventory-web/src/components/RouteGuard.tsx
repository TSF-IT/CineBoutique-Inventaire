import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { appStore } from '../lib/context/appContext';

export function GuardShop() {
  const loc = useLocation();
  const shop = appStore.getShop();
  if (!shop) return <Navigate to="/select-shop" replace state={{ from: loc }} />;
  return <Outlet />;
}
export function GuardOperator() {
  const loc = useLocation();
  const shop = appStore.getShop();
  const op = appStore.getOperator();
  if (!shop) return <Navigate to="/select-shop" replace state={{ from: loc }} />;
  if (!op) return <Navigate to="/select-user" replace state={{ from: loc }} />;
  return <Outlet />;
}
