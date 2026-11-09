import { AlertTriangle, CheckCircle2, Clock3, ListChecks } from 'lucide-react'
import { Badge } from '../../../shared/components/Badge'
import type { CacheStatus, StatsResult } from './boardMeta'

const cacheTone: Record<CacheStatus, 'emerald' | 'amber' | 'slate' | 'red'> = {
  HIT: 'emerald',
  MISS: 'amber',
  OFF: 'slate',
  DOWN: 'red',
}

function Stat({ icon: Icon, value, label, critical }: { icon: typeof ListChecks; value: string; label: string; critical?: boolean }) {
  return (
    <div className="flex min-w-0 items-center gap-3 px-4 py-4 sm:px-5">
      <span className={`grid h-9 w-9 shrink-0 place-items-center rounded-xl ${critical ? 'bg-red-50 text-red-600 dark:bg-red-400/10 dark:text-red-300' : 'bg-surface-muted text-muted'}`}>
        <Icon aria-hidden="true" className="h-4 w-4" />
      </span>
      <span>
        <span className="block text-xl font-semibold leading-none tracking-tight tabular-nums text-ink">{value}</span>
        <span className="mt-1.5 block text-[11px] font-medium text-muted">{label}</span>
      </span>
    </div>
  )
}

export function BoardStatsBar({ result }: { result: StatsResult | null }) {
  if (!result) {
    return <div className="h-[78px] animate-pulse rounded-2xl bg-surface-muted" aria-label="Loading board statistics" />
  }
  const { stats, cache } = result
  return (
    <div className="flex flex-col overflow-hidden rounded-2xl border border-line bg-surface sm:flex-row sm:items-center" aria-label="Board statistics">
      <div className="grid flex-1 grid-cols-2 sm:grid-cols-4">
        <Stat icon={ListChecks} value={String(stats.total)} label="Total tasks" />
        <Stat icon={Clock3} value={String(stats.doing)} label="In progress" />
        <Stat icon={CheckCircle2} value={String(stats.doneToday)} label="Done today" />
        <Stat icon={AlertTriangle} value={String(stats.overdue)} label="Overdue" critical={stats.overdue > 0} />
      </div>
      <div className="flex border-t border-line px-4 py-3 sm:border-l sm:border-t-0 sm:px-5"><Badge tone={cacheTone[cache]} title="Result of the latest Redis-backed stats request">cache {cache}</Badge></div>
    </div>
  )
}
