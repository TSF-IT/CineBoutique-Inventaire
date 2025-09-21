export const LoadingIndicator = ({ label }: { label?: string }) => (
  <div className="flex items-center justify-center gap-3 py-10 text-slate-300">
    <span className="h-4 w-4 animate-spin rounded-full border-2 border-slate-400 border-t-transparent" aria-hidden />
    <span>{label ?? 'Chargement en cours…'}</span>
  </div>
)
