export const LoadingIndicator = ({ label }: { label?: string }) => (
  <div className="flex items-center justify-center gap-3 py-10 text-(--cb-muted)">
    <span
      className="h-4 w-4 animate-spin rounded-full border-2 border-(--cb-border-strong) border-t-transparent"
      role="status"
      aria-hidden
    />
    <span>{label ?? 'Chargement en cours…'}</span>
  </div>
)
