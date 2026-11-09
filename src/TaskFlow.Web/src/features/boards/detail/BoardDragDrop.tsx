import { useEffect, useRef, useState } from 'react'
import { DragDropProvider, DragOverlay, KeyboardSensor, PointerSensor } from '@dnd-kit/react'
import { move } from '@dnd-kit/helpers'
import { TaskCardOverlay } from '../../tasks/card/TaskCard'
import { TASK_STATES, type TaskItem, type TaskState } from '../../tasks/model'
import { KanbanColumn } from './KanbanColumn'
import type { BoardDensity } from './useBoardDensity'

type Columns = Record<TaskState, string[]>

function groupTasks(tasks: TaskItem[]): Columns {
  return {
    Todo: tasks.filter((task) => task.status === 'Todo').map((task) => task.id),
    Doing: tasks.filter((task) => task.status === 'Doing').map((task) => task.id),
    Done: tasks.filter((task) => task.status === 'Done').map((task) => task.id),
  }
}

interface BoardDragDropProps {
  tasks: TaskItem[]
  canEdit: boolean
  density: BoardDensity
  pendingTaskIds: ReadonlySet<string>
  isMovePending: () => boolean
  onCreate: () => void
  onEdit: (task: TaskItem) => void
  onMove: (task: TaskItem, state: TaskState) => void
  onDelete: (task: TaskItem) => void
  onReorder: (task: TaskItem, state: TaskState, beforeTaskId: string | undefined, restoreFocus: boolean) => void
}

export function BoardDragDrop({ tasks, canEdit, density, pendingTaskIds, isMovePending, onCreate, onEdit, onMove, onDelete, onReorder }: BoardDragDropProps) {
  const [preview, setPreview] = useState<Columns | null>(null)
  const dragRef = useRef<{ task: TaskItem; initial: Columns; current: Columns } | null>(null)
  const boardRef = useRef<HTMLDivElement | null>(null)
  const focusAfterCancel = useRef<string | null>(null)
  const columns = preview ?? groupTasks(tasks)
  const tasksById = new Map(tasks.map((task) => [task.id, task]))

  useEffect(() => {
    if (preview || !focusAfterCancel.current) return
    const handle = document.querySelector<HTMLButtonElement>(`[data-task-drag-handle="${focusAfterCancel.current}"]`)
    focusAfterCancel.current = null
    if (document.activeElement === document.body || document.activeElement === handle) handle?.focus()
  }, [preview])

  return (
    <DragDropProvider
      sensors={[PointerSensor, KeyboardSensor]}
      onBeforeDragStart={(event) => {
        const task = tasksById.get(String(event.operation.source?.id))
        if (!canEdit || !task || isMovePending()) event.preventDefault()
      }}
      onDragStart={(event) => {
        const task = tasksById.get(String(event.operation.source?.id))
        if (!canEdit || !task || isMovePending()) return
        const initial = groupTasks(tasks)
        dragRef.current = { task, initial, current: initial }
        setPreview(initial)
      }}
      onDragOver={(event) => {
        const drag = dragRef.current
        if (!drag) return
        const next = move(drag.current, event)
        if (next === drag.current) return
        drag.current = next
        setPreview(next)
      }}
      onDragEnd={(event) => {
        const drag = dragRef.current
        dragRef.current = null
        if (drag && event.nativeEvent instanceof KeyboardEvent) focusAfterCancel.current = drag.task.id
        setPreview(null)
        if (!drag || event.canceled || !event.operation.target || !canEdit || isMovePending()) return
        const targetId = String(event.operation.target.id)
        if (!TASK_STATES.some((state) => state === targetId) && !tasksById.has(targetId)) return
        if (event.nativeEvent instanceof MouseEvent && boardRef.current) {
          const bounds = boardRef.current.getBoundingClientRect()
          const { clientX, clientY } = event.nativeEvent
          if (clientX < bounds.left || clientX > bounds.right || clientY < bounds.top || clientY > bounds.bottom) return
        }
        const state = TASK_STATES.find((value) => drag.current[value].includes(drag.task.id))
        if (!state) return
        const index = drag.current[state].indexOf(drag.task.id)
        if (state === drag.task.status && index === drag.initial[state].indexOf(drag.task.id)) return
        focusAfterCancel.current = null
        onReorder(drag.task, state, drag.current[state][index + 1], event.nativeEvent instanceof KeyboardEvent)
      }}
    >
      <div ref={boardRef} className="grid items-start gap-4 lg:grid-cols-3">
        {TASK_STATES.map((state) => (
          <KanbanColumn
            key={state}
            state={state}
            tasks={columns[state].flatMap((id) => tasksById.has(id) ? [tasksById.get(id)!] : [])}
            canEdit={canEdit}
            density={density}
            pendingTaskIds={pendingTaskIds}
            isDragActive={preview !== null}
            onCreate={onCreate}
            onEdit={onEdit}
            onMove={onMove}
            onDelete={onDelete}
          />
        ))}
      </div>
      <DragOverlay dropAnimation={null}>
        {(source) => {
          const task = tasksById.get(String(source.id))
          return task ? <TaskCardOverlay task={task} density={density} /> : null
        }}
      </DragOverlay>
    </DragDropProvider>
  )
}
