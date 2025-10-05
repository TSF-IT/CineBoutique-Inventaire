// tiny localStorage helpers
const keys = {
  shop: 'inv.shop',
  operator: 'inv.operator',
} as const;

export type ShopCtx = { id: string; name: string };
export type OperatorCtx = { id: string; name: string };

export const appStore = {
  getShop(): ShopCtx | null {
    try {
      const stored = localStorage.getItem(keys.shop);
      return stored ? (JSON.parse(stored) as ShopCtx) : null;
    } catch {
      return null;
    }
  },
  setShop(value: ShopCtx | null) {
    if (value) {
      localStorage.setItem(keys.shop, JSON.stringify(value));
      return;
    }
    localStorage.removeItem(keys.shop);
  },
  getOperator(): OperatorCtx | null {
    try {
      const stored = localStorage.getItem(keys.operator);
      return stored ? (JSON.parse(stored) as OperatorCtx) : null;
    } catch {
      return null;
    }
  },
  setOperator(value: OperatorCtx | null) {
    if (value) {
      localStorage.setItem(keys.operator, JSON.stringify(value));
      return;
    }
    localStorage.removeItem(keys.operator);
  },
  clearAll() {
    localStorage.removeItem(keys.shop);
    localStorage.removeItem(keys.operator);
  },
};
