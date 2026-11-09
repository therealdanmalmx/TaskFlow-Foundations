import { Activity as ActivityIcon, Database } from 'lucide-react'
import { relativeTime } from '../../../shared/lib/date'
import type { Activity } from './boardMeta'
import { RequestStatusNotice, type RequestStatus } from '../../../shared/components/RequestStatusNotice'

export function ActivityPanel({ activity, status, onRetry }: { activity: Activity | null; status: RequestStatus; onRetry: () => void }) {
  return (
    <aside className="self-start rounded-2xl border border-line bg-surface p-5 2xl:sticky 2xl:top-24">
      <div className="flex items-center justify-between gap-3">
        <h2 className="flex items-center gap-2 text-sm font-semibold text-ink"><ActivityIcon aria-hidden="true" className="h-4 w-4 text-brand" /> Recent activity</h2>
        <span className="flex items-center gap-1 text-[10px] font-medium text-faint" title="Recent activity is stored ephemerally in Redis"><Database aria-hidden="true" className="h-3 w-3" /> Redis</span>
      </div>

      <div className="mt-4"><RequestStatusNotice status={status} label="Activity" hasPreviousData={activity !== null} onRetry={onRetry} /></div>
      {!activity && status === 'loading' && <div className="mt-4 grid gap-3" aria-label="Loading activity"><div className="h-12 animate-pulse rounded-xl bg-surface-muted" /><div className="h-12 animate-pulse rounded-xl bg-surface-muted" /></div>}
      {activity && !activity.available && (
        <div className="mt-4 rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm leading-6 text-amber-900 dark:border-amber-400/20 dark:bg-amber-400/10 dark:text-amber-200">
          Activity is temporarily offline because the cache is unavailable. Your board still works.
        </div>
      )}
      {activity?.available && activity.entries.length === 0 && <p className="mt-4 rounded-xl bg-surface-muted p-4 text-sm leading-6 text-muted">No activity yet. Create or move a task to get things started.</p>}
      {activity?.available && activity.entries.length > 0 && (
        <ol className="ml-1 mt-5">
          {activity.entries.slice(0, 10).map((entry, index) => (
            <li key={`${entry.at}-${index}`} className="relative border-l border-line pb-5 pl-5 last:border-transparent last:pb-0">
              <span aria-hidden="true" className={`absolute -left-[4.5px] top-1.5 h-2 w-2 rounded-full ring-4 ring-surface ${index === 0 ? 'bg-accent' : 'bg-line-strong'}`} />
              <p className="break-words text-xs leading-5 text-muted">{entry.message}</p>
              <time dateTime={entry.at} title={entry.at} className="mt-1 block text-[10px] text-faint">{relativeTime(entry.at)}</time>
            </li>
          ))}
        </ol>
      )}
    </aside>
  )
}
