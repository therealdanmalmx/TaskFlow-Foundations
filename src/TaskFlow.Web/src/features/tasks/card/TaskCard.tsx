import { CalendarDays, ChevronLeft, ChevronRight, GripVertical, Pencil, Trash2, UserRound } from 'lucide-react'
import { useSortable } from '@dnd-kit/react/sortable'
import { Avatar } from '../../../shared/components/Avatar'
import { Badge } from '../../../shared/components/Badge'
import { Button } from '../../../shared/components/Button'
import { formatShortDate, isOverdue } from '../../../shared/lib/date'
import { TASK_STATES, type TaskItem, type TaskPriority, type TaskState } from '../model'
import type { BoardDensity } from '../../boards/detail/useBoardDensity'

const priorityTone: Record<TaskPriority, 'slate' | 'blue' | 'amber' | 'red'> = {
  Low: 'slate',
  Medium: 'blue',
  High: 'amber',
  Urgent: 'red',
}

interface TaskCardProps {
  task: TaskItem
  index: number
  column: TaskState
  canEdit: boolean
  density: BoardDensity
  isPending: boolean
  onEdit: (task: TaskItem) => void
  onMove: (task: TaskItem, state: TaskState) => void
  onDelete: (task: TaskItem) => void
}

interface TaskCardContentProps {
  task: TaskItem
  canEdit: boolean
  density: BoardDensity
  isPending: boolean
  handleRef?: (element: Element | null) => void
  onEdit?: (task: TaskItem) => void
  onMove?: (task: TaskItem, state: TaskState) => void
  onDelete?: (task: TaskItem) => void
}

export function TaskCard({ task, index, column, canEdit, density, isPending, onEdit, onMove, onDelete }: TaskCardProps) {
  const { ref, handleRef, isDragSource } = useSortable({
    id: task.id,
    index,
    group: column,
    type: 'task',
    accept: 'task',
    disabled: !canEdit || isPending,
  })

  return (
    <li ref={ref} aria-busy={isPending} data-task-id={task.id} className={`group min-w-0 rounded-xl border transition-[border-color,box-shadow] duration-200 ${density === 'compact' ? 'p-3' : 'p-4'} ${isDragSource ? 'border-dashed border-brand/60 bg-brand/5 shadow-inner' : 'border-line bg-surface shadow-[0_2px_5px_-3px_rgb(15_23_42_/_0.16)] hover:border-line-strong hover:shadow-[0_5px_18px_-8px_rgb(15_23_42_/_0.2)] dark:shadow-none'}`}>
      <div className={isDragSource ? 'pointer-events-none opacity-0' : undefined}>
        <TaskCardContent task={task} canEdit={canEdit} density={density} isPending={isPending} handleRef={handleRef} onEdit={onEdit} onMove={onMove} onDelete={onDelete} />
      </div>
    </li>
  )
}

export function TaskCardOverlay({ task, density }: { task: TaskItem; density: BoardDensity }) {
  return (
    <div inert aria-hidden="true" data-task-drag-overlay={task.id} className={`pointer-events-none group min-w-0 cursor-grabbing rounded-xl border border-brand/60 bg-surface shadow-[0_18px_40px_-12px_rgb(15_23_42_/_0.35)] ring-2 ring-brand/15 dark:shadow-[0_18px_40px_-12px_rgb(0_0_0_/_0.6)] ${density === 'compact' ? 'p-3' : 'p-4'}`}>
      <TaskCardContent task={task} canEdit density={density} isPending={false} />
    </div>
  )
}

function TaskCardContent({ task, canEdit, density, isPending, handleRef, onEdit, onMove, onDelete }: TaskCardContentProps) {
  const stateIndex = TASK_STATES.indexOf(task.status)
  const previousState = TASK_STATES[stateIndex - 1]
  const nextState = TASK_STATES[stateIndex + 1]
  const overdue = isOverdue(task.dueDate, task.status === 'Done')
  const compact = density === 'compact'
  const moveControls = canEdit && (
    <div className={compact ? 'ml-auto flex items-center gap-1' : 'mt-3 flex items-center justify-between border-t border-line pt-2'}>
      {previousState ? (
        <Button variant="ghost" size="sm" className="min-h-8 px-2 text-xs" title={`Move to ${previousState}`} onClick={() => onMove?.(task, previousState)} disabled={isPending} aria-label={`Move ${task.title} to ${previousState}`}>
          <ChevronLeft aria-hidden="true" className="h-3.5 w-3.5" /> {!compact && previousState}
        </Button>
      ) : !compact && <span />}
      {nextState && (
        <Button variant="ghost" size="sm" className="min-h-8 px-2 text-xs" title={`Move to ${nextState}`} onClick={() => onMove?.(task, nextState)} disabled={isPending} aria-label={`Move ${task.title} to ${nextState}`}>
          {!compact && nextState} <ChevronRight aria-hidden="true" className="h-3.5 w-3.5" />
        </Button>
      )}
    </div>
  )

  return (
    <>
      <div className="flex items-start justify-between gap-3">
        <Badge tone={priorityTone[task.priority]}>{task.priority}</Badge>
        {canEdit && (
          <div className="-mr-1 -mt-1 flex items-center">
            <div className="flex opacity-100 transition sm:opacity-0 sm:group-hover:opacity-100 sm:group-focus-within:opacity-100">
              <Button variant="ghost" size="icon" className="h-8 w-8 rounded-lg" onClick={() => onEdit?.(task)} disabled={isPending} aria-label={`Edit ${task.title}`}>
                <Pencil aria-hidden="true" className="h-3.5 w-3.5" />
              </Button>
              <Button variant="ghost" size="icon" className="h-8 w-8 rounded-lg text-faint hover:bg-red-50 hover:text-red-700 dark:hover:bg-red-400/10 dark:hover:text-red-300" onClick={() => onDelete?.(task)} disabled={isPending} aria-label={`Delete ${task.title}`}>
                <Trash2 aria-hidden="true" className="h-3.5 w-3.5" />
              </Button>
            </div>
            <button ref={handleRef} type="button" data-task-drag-handle={handleRef ? task.id : undefined} disabled={isPending} aria-label={`Drag ${task.title}`} title="Drag to reorder or move. Keyboard: Space, arrow keys, Space to drop, Escape to cancel." className="grid h-8 w-8 touch-none select-none place-items-center rounded-lg text-faint hover:bg-surface-muted hover:text-ink enabled:cursor-grab enabled:active:cursor-grabbing disabled:opacity-40">
              <GripVertical aria-hidden="true" className="h-4 w-4" />
            </button>
          </div>
        )}
      </div>

      <button type="button" disabled={!canEdit || isPending} onClick={() => onEdit?.(task)} title={task.title} className={`${compact ? 'mt-1' : 'mt-3'} block w-full rounded-md text-left disabled:cursor-default`}>
        <h3 className={`${compact ? 'line-clamp-2' : ''} break-words text-[13px] font-semibold leading-[1.65] ${task.status === 'Done' ? 'text-muted line-through decoration-line-strong' : 'text-ink'}`}>{task.title}</h3>
        {!compact && task.description && <p className="mt-1.5 line-clamp-2 break-words text-xs leading-5 text-muted">{task.description}</p>}
      </button>

      <div className={`${compact ? 'mt-2' : 'mt-4'} flex flex-wrap items-center gap-2 text-[11px] text-muted`}>
        {task.dueDate && (
          <span className={`flex items-center gap-1.5 rounded-md px-1.5 py-1 font-medium ${overdue ? 'bg-red-50 text-red-700 dark:bg-red-400/10 dark:text-red-300' : 'bg-surface-muted text-muted'}`} title={`Due ${task.dueDate}`}>
            <CalendarDays aria-hidden="true" className="h-3.5 w-3.5" /> {formatShortDate(task.dueDate)}
          </span>
        )}
        <span className={`${compact ? '' : 'ml-auto'} flex items-center gap-1.5`} title={task.assigneeName ? `Assigned to ${task.assigneeName}` : 'Unassigned'}>
          {task.assigneeName ? <><Avatar name={task.assigneeName} size="sm" /><span className="sr-only">Assigned to {task.assigneeName}</span></> : <span className="grid h-7 w-7 place-items-center rounded-full border border-dashed border-line-strong"><UserRound aria-hidden="true" className="h-3.5 w-3.5 text-faint" /></span>}
        </span>
        {compact && moveControls}
      </div>

      {!compact && moveControls}
    </>
  )
}
