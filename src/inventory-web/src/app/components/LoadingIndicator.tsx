export const LoadingIndicator = ({ label }: { label?: string }) => (
  <div className="flex items-center justify-center gap-3 py-10 text-slate-500 dark:text-slate-300">
    <span
      className="h-4 w-4 animate-spin rounded-full border-2 border-slate-300 border-t-transparent dark:border-slate-400"
      aria-hidden
    />
    <span>{label ?? 'Chargement en cours…'}</span>
  </div>
)
