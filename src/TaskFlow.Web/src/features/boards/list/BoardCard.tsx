import { Archive, ArrowUpRight, LayoutGrid, ListTodo } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../shared/components/Badge'
import { relativeTime } from '../../../shared/lib/date'
import type { BoardSummary } from '../model'

export function BoardCard({ board }: { board: BoardSummary }) {
  return (
    <Link
      to={`/workspaces/${board.workspaceId}/boards/${board.id}`}
      className="group relative flex min-h-60 flex-col overflow-hidden rounded-2xl border border-line bg-surface p-6 shadow-[0_2px_5px_-3px_rgb(15_23_42_/_0.12)] transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-lg hover:shadow-slate-900/5 dark:shadow-none dark:hover:bg-surface-raised"
    >
      <span aria-hidden="true" className="absolute left-0 top-7 h-10 w-[3px] rounded-r-full" style={{ backgroundColor: board.color }} />
      <div className="flex items-start justify-between gap-4">
        <span className="relative grid h-12 w-12 place-items-center overflow-hidden rounded-2xl" style={{ color: board.color }}>
          <span aria-hidden="true" className="absolute inset-0 opacity-[0.12]" style={{ backgroundColor: board.color }} />
          <LayoutGrid aria-hidden="true" className="relative h-5 w-5" />
        </span>
        <span className="grid h-8 w-8 place-items-center rounded-full bg-surface-muted transition group-hover:bg-accent/10"><ArrowUpRight aria-hidden="true" className="h-4 w-4 text-faint transition group-hover:-translate-y-0.5 group-hover:translate-x-0.5 group-hover:text-brand" /></span>
      </div>
      <h2 className="mt-5 break-words text-lg font-semibold tracking-tight text-ink">{board.name}</h2>
      <p className="mt-1.5 line-clamp-2 break-words text-sm leading-6 text-muted">{board.description || 'No description yet.'}</p>
      <div className="mt-6 flex flex-1 items-end">
      <div className="flex w-full flex-wrap items-center justify-between gap-3 border-t border-line pt-4 text-[11px] text-muted">
        <span className="flex items-center gap-1.5 font-medium">
          <ListTodo aria-hidden="true" className="h-3.5 w-3.5 text-faint" />
          {board.taskCount} {board.taskCount === 1 ? 'task' : 'tasks'}
        </span>
        {board.isArchived ? (
          <Badge><Archive aria-hidden="true" className="mr-1 h-3 w-3" /> Archived</Badge>
        ) : (
          <span title={board.updatedAt}>Updated {relativeTime(board.updatedAt)}</span>
        )}
      </div>
      </div>
    </Link>
  )
}
