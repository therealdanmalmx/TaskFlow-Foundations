export function LoadingState({ label = 'Loading…' }: { label?: string }) {
  return (
    <div role="status" className="grid min-h-48 place-items-center text-sm font-medium text-muted">
      <span className="flex items-center gap-3">
        <span aria-hidden="true" className="h-5 w-5 animate-spin rounded-full border-2 border-line-strong border-t-accent" />
        {label}
      </span>
    </div>
  )
}
