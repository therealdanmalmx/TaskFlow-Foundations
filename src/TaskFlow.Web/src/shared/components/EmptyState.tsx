import type { LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'

export function EmptyState({ icon: Icon, title, description, action }: { icon: LucideIcon; title: string; description: string; action?: ReactNode }) {
  return (
    <div className="grid min-h-64 place-items-center rounded-2xl border border-dashed border-line-strong bg-surface/60 p-8 text-center">
      <div className="max-w-sm">
        <span className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-2xl bg-surface-muted text-muted">
          <Icon aria-hidden="true" className="h-6 w-6" />
        </span>
        <h2 className="font-bold text-ink">{title}</h2>
        <p className="mt-1 text-sm leading-6 text-muted">{description}</p>
        {action && <div className="mt-5">{action}</div>}
      </div>
    </div>
  )
}
