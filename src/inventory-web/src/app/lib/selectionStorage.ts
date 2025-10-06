export const STORAGE_KEYS = {
  shopId: 'selectedShopId',
  userId: 'selectedUserId',
  userName: 'selectedUserName',
} as const;

export function setSelectedShop(id: string) {
  localStorage.setItem(STORAGE_KEYS.shopId, id);
  // reset user when shop changes
  localStorage.removeItem(STORAGE_KEYS.userId);
  localStorage.removeItem(STORAGE_KEYS.userName);
}
export function getSelectedShop(): string | null {
  return localStorage.getItem(STORAGE_KEYS.shopId);
}
export function setSelectedUser(id: string, name: string | null) {
  localStorage.setItem(STORAGE_KEYS.userId, id);
  if (name) localStorage.setItem(STORAGE_KEYS.userName, name);
}
export function getSelectedUser(): { id: string | null; name: string | null } {
  return {
    id: localStorage.getItem(STORAGE_KEYS.userId),
    name: localStorage.getItem(STORAGE_KEYS.userName),
  };
}
export function clearSelections() {
  localStorage.removeItem(STORAGE_KEYS.shopId);
  localStorage.removeItem(STORAGE_KEYS.userId);
  localStorage.removeItem(STORAGE_KEYS.userName);
}
