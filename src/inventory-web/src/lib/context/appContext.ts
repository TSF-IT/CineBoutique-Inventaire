// tiny localStorage helpers
const k = {
  shop: 'inv.shop',
  operator: 'inv.operator'
};
export type ShopCtx = { id: string; name: string };
export type OperatorCtx = { id: string; name: string };

export const appStore = {
  getShop(): ShopCtx | null {
    try { const s = localStorage.getItem(k.shop); return s ? JSON.parse(s) : null; } catch { return null; }
  },
  setShop(v: ShopCtx | null) { v ? localStorage.setItem(k.shop, JSON.stringify(v)) : localStorage.removeItem(k.shop); },
  getOperator(): OperatorCtx | null {
    try { const s = localStorage.getItem(k.operator); return s ? JSON.parse(s) : null; } catch { return null; }
  },
  setOperator(v: OperatorCtx | null) { v ? localStorage.setItem(k.operator, JSON.stringify(v)) : localStorage.removeItem(k.operator); },
  clearAll() { localStorage.removeItem(k.shop); localStorage.removeItem(k.operator); }
};
