import { Check, Circle, CircleDashed, Plus } from 'lucide-react'
import { useDroppable } from '@dnd-kit/react'
import { Button } from '../../../shared/components/Button'
import { TaskCard } from '../../tasks/card/TaskCard'
import type { TaskItem, TaskState } from '../../tasks/model'
import type { BoardDensity } from './useBoardDensity'

const columnStyle: Record<TaskState, { icon: typeof Circle; tone: string; label: string }> = {
  Todo: { icon: CircleDashed, tone: 'text-muted', label: 'Ready when you are' },
  Doing: { icon: Circle, tone: 'text-blue-600 dark:text-blue-400', label: 'Work in motion' },
  Done: { icon: Check, tone: 'text-emerald-600 dark:text-emerald-400', label: 'A little progress, every day' },
}

interface KanbanColumnProps {
  state: TaskState
  tasks: TaskItem[]
  canEdit: boolean
  density: BoardDensity
  pendingTaskIds: ReadonlySet<string>
  isDragActive?: boolean
  onCreate: () => void
  onEdit: (task: TaskItem) => void
  onMove: (task: TaskItem, state: TaskState) => void
  onDelete: (task: TaskItem) => void
}

export function KanbanColumn({ state, tasks, canEdit, density, pendingTaskIds, isDragActive = false, onCreate, onEdit, onMove, onDelete }: KanbanColumnProps) {
  const style = columnStyle[state]
  const StatusIcon = style.icon
  const { ref, isDropTarget } = useDroppable({ id: state, accept: 'task', collisionPriority: -1, disabled: !canEdit })
  return (
    <section ref={ref} aria-labelledby={`column-${state}`} className={`flex min-w-0 flex-col rounded-2xl border p-3 transition-colors lg:min-h-[32rem] ${isDropTarget ? 'border-brand bg-brand/10 ring-2 ring-brand/20' : isDragActive && canEdit ? 'border-brand/30 bg-surface-muted/60' : 'border-line/70 bg-surface-muted/60'}`}>
      <header className="relative flex items-center gap-2.5 px-1 pb-5 pt-2">
        <span className={`grid h-7 w-7 shrink-0 place-items-center rounded-lg border border-line bg-surface ${style.tone}`}><StatusIcon aria-hidden="true" className="h-3.5 w-3.5" /></span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2"><h2 id={`column-${state}`} className="text-sm font-semibold text-ink">{state}</h2><span className="text-xs font-medium tabular-nums text-faint">{tasks.length}</span></div>
          <p className={`mt-0.5 text-[11px] ${isDragActive && canEdit ? 'font-medium text-brand' : 'text-muted'}`}>{isDragActive && canEdit ? 'Drag to choose a position' : style.label}</p>
        </div>
        {canEdit && state === 'Todo' && (
          <Button variant="ghost" size="icon" className="ml-auto h-8 w-8 rounded-lg" disabled={pendingTaskIds.size > 0 || isDragActive} onClick={onCreate} aria-label="Create task">
            <Plus aria-hidden="true" className="h-4 w-4" />
          </Button>
        )}
      </header>
      <ul className={`grid flex-1 content-start pb-2 ${density === 'compact' ? 'gap-2' : 'gap-3'}`}>
        {tasks.map((task, index) => (
          <TaskCard key={task.id} task={task} index={index} column={state} canEdit={canEdit} density={density} isPending={pendingTaskIds.has(task.id)} onEdit={onEdit} onMove={onMove} onDelete={onDelete} />
        ))}
        {tasks.length === 0 && (
          <li className={`grid min-h-36 place-items-center rounded-xl border border-dashed bg-surface/40 p-5 text-center text-sm leading-6 ${isDragActive && canEdit ? 'border-brand/50 text-brand' : 'border-line-strong text-muted'}`}>
            {isDragActive && canEdit ? 'Drop a task here' : state === 'Todo' ? 'New tasks will start here.' : state === 'Doing' ? 'Move active work here.' : 'Completed work will land here.'}
          </li>
        )}
      </ul>
    </section>
  )
}
