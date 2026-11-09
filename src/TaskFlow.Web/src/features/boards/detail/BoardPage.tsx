import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Archive, ArrowLeft, Filter, LayoutGrid, Plus, Search, Settings2 } from 'lucide-react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Button } from '../../../shared/components/Button'
import { ConfirmDialog } from '../../../shared/components/ConfirmDialog'
import { LoadingState } from '../../../shared/components/LoadingState'
import { RequestStatusNotice, type RequestStatus } from '../../../shared/components/RequestStatusNotice'
import { NotFoundPage } from '../../../app/NotFoundPage'
import { parseEntityId } from '../../../shared/lib/entityId'
import { getMembers } from '../../members/list/api'
import type { Member } from '../../members/model'
import { createTask } from '../../tasks/create/api'
import { deleteTask } from '../../tasks/delete/api'
import { updateTask } from '../../tasks/edit/api'
import { TaskEditorDialog } from '../../tasks/form/TaskEditorDialog'
import { moveTask } from '../../tasks/move/api'
import { TASK_PRIORITIES, type TaskInput, type TaskItem, type TaskPriority, type TaskState } from '../../tasks/model'
import { canEditWorkspace, canManageWorkspace } from '../../workspaces/model'
import type { BoardDetail, BoardInput } from '../model'
import { ActivityPanel } from './ActivityPanel'
import { BoardSettingsDialog } from './BoardSettingsDialog'
import { BoardStatsBar } from './BoardStatsBar'
import { getBoard, setBoardArchived, updateBoard } from './api'
import { getBoardActivity, getBoardStats, type Activity, type StatsResult } from './boardMeta'
import { BoardDragDrop } from './BoardDragDrop'
import { BoardDensitySwitch } from './BoardDensitySwitch'
import { useBoardDensity } from './useBoardDensity'

type PriorityFilter = 'All' | TaskPriority
type BoardDetailPart = 'stats' | 'activity' | 'members'

export function BoardPage() {
  const { workspaceId: workspaceIdParam, boardId: boardIdParam } = useParams()
  const workspaceId = parseEntityId(workspaceIdParam)
  const boardId = parseEntityId(boardIdParam)

  if (!workspaceId || !boardId) {
    return <NotFoundPage />
  }

  return <BoardPageContent key={`${workspaceId}:${boardId}`} workspaceId={workspaceId} boardId={boardId} />
}

function BoardPageContent({ workspaceId, boardId }: { workspaceId: string; boardId: string }) {
  const navigate = useNavigate()
  const [board, setBoard] = useState<BoardDetail | null>(null)
  const [stats, setStats] = useState<StatsResult | null>(null)
  const [activity, setActivity] = useState<Activity | null>(null)
  const [members, setMembers] = useState<Member[]>([])
  const [detailStatus, setDetailStatus] = useState<Record<BoardDetailPart, RequestStatus>>({ stats: 'loading', activity: 'loading', members: 'loading' })
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [priority, setPriority] = useState<PriorityFilter>('All')
  const [assignee, setAssignee] = useState('All')
  const [editorOpen, setEditorOpen] = useState(false)
  const [editingTask, setEditingTask] = useState<TaskItem | null>(null)
  const [deleteCandidate, setDeleteCandidate] = useState<TaskItem | null>(null)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [pendingTaskIds, setPendingTaskIds] = useState<Set<string>>(new Set())
  const [needsRefresh, setNeedsRefresh] = useState(false)
  const pendingTaskIdsRef = useRef(new Set<string>())
  const focusTaskRef = useRef<string | null>(null)
  const { density, setDensity } = useBoardDensity()
  const [isDeleting, setIsDeleting] = useState(false)
  const activeRef = useRef(true)
  const loadGenerationRef = useRef(0)
  const loadControllerRef = useRef<AbortController | null>(null)
  const detailGenerationRef = useRef({ stats: 0, activity: 0, members: 0 })

  const isCurrentBoard = useCallback((targetBoardId: string) => (
    activeRef.current &&
    targetBoardId === boardId &&
    window.location.pathname.replace(/\/+$/, '').toLowerCase() === `/workspaces/${workspaceId}/boards/${boardId}`
  ), [boardId, workspaceId])

  const refreshDetails = useCallback(async (parts: BoardDetailPart[], signal?: AbortSignal) => {
    await Promise.all(parts.map(async (part) => {
      const generation = ++detailGenerationRef.current[part]
      const isCurrentRequest = () => !signal?.aborted && isCurrentBoard(boardId) && generation === detailGenerationRef.current[part]
      setDetailStatus((current) => ({ ...current, [part]: 'loading' }))
      try {
        switch (part) {
          case 'stats': {
            const result = await getBoardStats(boardId, signal)
            if (isCurrentRequest()) setStats(result)
            break
          }
          case 'activity': {
            const result = await getBoardActivity(boardId, signal)
            if (isCurrentRequest()) setActivity(result)
            break
          }
          case 'members': {
            const result = await getMembers(workspaceId, signal)
            if (isCurrentRequest()) setMembers(result)
            break
          }
        }
        if (isCurrentRequest()) setDetailStatus((current) => ({ ...current, [part]: 'ready' }))
      } catch {
        if (isCurrentRequest()) setDetailStatus((current) => ({ ...current, [part]: 'error' }))
      }
    }))
  }, [boardId, isCurrentBoard, workspaceId])

  const loadBoard = useCallback(async () => {
    const generation = ++loadGenerationRef.current
    loadControllerRef.current?.abort()
    const controller = new AbortController()
    loadControllerRef.current = controller
    setIsLoading(true)
    setError(null)
    setBoard(null)
    setStats(null)
    setActivity(null)
    setMembers([])
    setEditorOpen(false)
    setEditingTask(null)
    setDeleteCandidate(null)
    setDeleteError(null)
    setSettingsOpen(false)
    setPendingTaskIds(new Set())
    setNeedsRefresh(false)
    pendingTaskIdsRef.current.clear()
    setIsDeleting(false)
    try {
      const loadedBoard = await getBoard(boardId, controller.signal)
      if (controller.signal.aborted || !isCurrentBoard(boardId) || generation !== loadGenerationRef.current) return
      if (loadedBoard.workspaceId !== workspaceId) {
        loadGenerationRef.current += 1
        navigate(`/workspaces/${loadedBoard.workspaceId}/boards/${loadedBoard.id}`, { replace: true })
        return
      }
      setBoard(loadedBoard)
      await refreshDetails(['stats', 'activity', 'members'], controller.signal)
    } catch (requestError) {
      if (controller.signal.aborted || !isCurrentBoard(boardId) || generation !== loadGenerationRef.current) return
      setError(apiErrorMessage(requestError, 'Could not load the board.'))
    } finally {
      if (isCurrentBoard(boardId) && generation === loadGenerationRef.current) setIsLoading(false)
    }
  }, [boardId, isCurrentBoard, navigate, refreshDetails, workspaceId])

  useEffect(() => {
    activeRef.current = true
    void loadBoard()
    return () => {
      activeRef.current = false
      loadGenerationRef.current += 1
      loadControllerRef.current?.abort()
    }
  }, [loadBoard])

  // A saved move remounts the card in another column after the drag has ended.
  // Restore keyboard focus only then, without taking it from another control.
  useEffect(() => {
    const taskId = focusTaskRef.current
    if (taskId === null || pendingTaskIds.has(taskId)) return
    focusTaskRef.current = null
    const handle = document.querySelector<HTMLButtonElement>(`[data-task-drag-handle="${taskId}"]`)
    if (document.activeElement === document.body || document.activeElement === handle) handle?.focus()
  }, [pendingTaskIds])

  async function refreshMeta(targetBoardId: string) {
    if (!isCurrentBoard(targetBoardId)) return
    await refreshDetails(['stats', 'activity'])
  }

  function setPending(taskId: string, pending: boolean) {
    if (pending) pendingTaskIdsRef.current.add(taskId)
    else pendingTaskIdsRef.current.delete(taskId)
    setPendingTaskIds((current) => {
      const next = new Set(current)
      if (pending) next.add(taskId)
      else next.delete(taskId)
      return next
    })
  }

  function openCreate() {
    setEditingTask(null)
    setEditorOpen(true)
  }

  function openEdit(task: TaskItem) {
    setEditingTask(task)
    setEditorOpen(true)
  }

  async function saveTask(input: TaskInput) {
    const targetBoardId = board?.id
    const targetTask = editingTask
    if (!targetBoardId || !isCurrentBoard(targetBoardId) || (targetTask && targetTask.boardId !== targetBoardId)) return
    if (targetTask) {
      const updated = await updateTask(targetTask.id, input)
      if (!isCurrentBoard(targetBoardId)) return
      setBoard((current) => current?.id === targetBoardId ? { ...current, tasks: current.tasks.map((task) => task.id === updated.id ? updated : task), updatedAt: updated.updatedAt } : current)
    } else {
      const created = await createTask(targetBoardId, input)
      if (!isCurrentBoard(targetBoardId)) return
      setBoard((current) => current?.id === targetBoardId ? { ...current, tasks: [...current.tasks, created], taskCount: current.taskCount + 1, updatedAt: created.updatedAt } : current)
    }
    void refreshMeta(targetBoardId)
  }

  async function move(task: TaskItem, state: TaskState) {
    const targetBoardId = board?.id
    if (!board || !targetBoardId || board.isArchived || !canEditWorkspace(board.role) ||
        task.status === state || task.boardId !== targetBoardId || needsRefresh ||
        !isCurrentBoard(targetBoardId) || pendingTaskIdsRef.current.size > 0) return
    setPending(task.id, true)
    setError(null)
    try {
      const moved = await moveTask(task.id, state)
      if (!isCurrentBoard(targetBoardId)) return
      setBoard((current) => current?.id === targetBoardId ? { ...current, tasks: current.tasks.map((item) => item.id === moved.id ? moved : item), updatedAt: moved.updatedAt } : current)
      void refreshMeta(targetBoardId)
    } catch (requestError) {
      if (!isCurrentBoard(targetBoardId)) return
      setError(apiErrorMessage(requestError, 'Could not move the task.'))
    } finally {
      if (isCurrentBoard(targetBoardId)) {
        setPending(task.id, false)
      }
    }
  }

  async function reorder(task: TaskItem, state: TaskState, beforeTaskId: string | undefined, restoreFocus: boolean) {
    if (!board || board.isArchived || !canEditWorkspace(board.role) || needsRefresh ||
        task.boardId !== board.id || !isCurrentBoard(board.id) || pendingTaskIdsRef.current.size > 0) return
    const previous = board
    const destination = board.tasks.filter((item) => item.status === state && item.id !== task.id)
      .sort((a, b) => a.position - b.position || a.id.localeCompare(b.id))
    const index = beforeTaskId ? destination.findIndex((item) => item.id === beforeTaskId) : destination.length
    if (index < 0) return
    destination.splice(index, 0, { ...task, status: state })
    const positions = new Map(destination.map((item, position) => [item.id, position]))
    setPending(task.id, true)
    setError(null)
    setBoard({ ...board, tasks: board.tasks.map((item) => positions.has(item.id)
      ? { ...item, status: state, position: positions.get(item.id)! }
      : item) })
    try {
      const moved = await moveTask(task.id, state, beforeTaskId ? 'before' : 'bottom', beforeTaskId)
      if (!isCurrentBoard(previous.id)) return
      setBoard((current) => current?.id === previous.id ? { ...current, tasks: current.tasks.map((item) => item.id === moved.id ? moved : item), updatedAt: moved.updatedAt } : current)
      try {
        const refreshed = await getBoard(previous.id)
        if (isCurrentBoard(previous.id)) setBoard(refreshed)
      } catch {
        if (isCurrentBoard(previous.id)) {
          setNeedsRefresh(true)
          setError('Task moved, but the board could not be refreshed. Reload the board before moving another task.')
        }
      }
      void refreshMeta(previous.id)
    } catch (requestError) {
      if (!isCurrentBoard(previous.id)) return
      setBoard(previous)
      setError(apiErrorMessage(requestError, 'Could not move the task. Your original order has been restored.'))
    } finally {
      if (isCurrentBoard(previous.id)) {
        if (restoreFocus) focusTaskRef.current = task.id
        setPending(task.id, false)
      }
    }
  }

  async function confirmDelete() {
    const candidate = deleteCandidate
    const targetBoardId = board?.id
    if (!candidate || !targetBoardId || candidate.boardId !== targetBoardId || !isCurrentBoard(targetBoardId)) return
    setIsDeleting(true)
    setDeleteError(null)
    try {
      await deleteTask(candidate.id)
      if (!isCurrentBoard(targetBoardId)) return
      setBoard((current) => current?.id === targetBoardId ? { ...current, tasks: current.tasks.filter((task) => task.id !== candidate.id), taskCount: Math.max(0, current.taskCount - 1) } : current)
      setDeleteCandidate(null)
      void refreshMeta(targetBoardId)
    } catch (requestError) {
      if (!isCurrentBoard(targetBoardId)) return
      setDeleteError(apiErrorMessage(requestError, 'Could not delete the task.'))
    } finally {
      if (isCurrentBoard(targetBoardId)) setIsDeleting(false)
    }
  }

  async function saveBoard(input: BoardInput) {
    const targetBoardId = board?.id
    if (!targetBoardId || !isCurrentBoard(targetBoardId)) return
    const updated = await updateBoard(targetBoardId, input)
    if (isCurrentBoard(targetBoardId)) setBoard(updated)
  }

  async function archiveBoard(isArchived: boolean) {
    const targetBoardId = board?.id
    if (!targetBoardId || !isCurrentBoard(targetBoardId)) return
    const updated = await setBoardArchived(targetBoardId, isArchived)
    if (!isCurrentBoard(targetBoardId)) return
    setBoard(updated)
    if (isArchived) setEditorOpen(false)
  }

  const assignees = useMemo(() => {
    const values = new Map<string, string>()
    for (const task of board?.tasks ?? []) if (task.assigneeId && task.assigneeName) values.set(task.assigneeId, task.assigneeName)
    return [...values.entries()].sort((a, b) => a[1].localeCompare(b[1]))
  }, [board?.tasks])

  const visibleTasks = useMemo(() => {
    const query = search.trim().toLocaleLowerCase()
    return (board?.tasks ?? [])
      .filter((task) => !query || `${task.title} ${task.description} ${task.assigneeName ?? ''}`.toLocaleLowerCase().includes(query))
      .filter((task) => priority === 'All' || task.priority === priority)
      .filter((task) => assignee === 'All' || (assignee === 'Unassigned' ? task.assigneeId === null : task.assigneeId === assignee))
      .sort((a, b) => a.position - b.position || a.id.localeCompare(b.id))
  }, [assignee, board?.tasks, priority, search])

  if (isLoading) return <LoadingState label="Loading board…" />
  if (!board) {
    return (
      <div className="mx-auto max-w-2xl py-12">
        <Alert>{error ?? 'This board is unavailable or you do not have access.'}</Alert>
        <div className="mt-4 flex gap-3"><Button onClick={() => void loadBoard()}>Retry</Button><Button variant="secondary" onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}>Back to boards</Button></div>
      </div>
    )
  }

  const canEdit = canEditWorkspace(board.role) && !board.isArchived
  const canManage = canManageWorkspace(board.role)
  const changesPending = pendingTaskIds.size > 0 || needsRefresh
  const disabledTaskIds = changesPending ? new Set(board.tasks.map((task) => task.id)) : pendingTaskIds

  return (
    <div className="mx-auto max-w-[100rem]">
      <header>
        <Link to={`/workspaces/${board.workspaceId}/boards`} className="inline-flex items-center gap-1.5 text-xs font-medium text-muted transition hover:text-ink"><ArrowLeft aria-hidden="true" className="h-3.5 w-3.5" /> All boards</Link>
        <div className="mt-5 flex flex-col gap-5 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex min-w-0 items-start gap-3.5">
            <span className="mt-0.5 grid h-12 w-12 shrink-0 place-items-center rounded-2xl text-white shadow-sm" style={{ backgroundColor: board.color }}><LayoutGrid aria-hidden="true" className="h-5 w-5" /></span>
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-2">
                <h1 className="break-words text-2xl font-semibold tracking-tight text-ink sm:text-3xl">{board.name}</h1>
                {board.isArchived && <span className="inline-flex items-center gap-1 rounded-full bg-surface-muted px-2 py-1 text-xs font-semibold text-muted"><Archive aria-hidden="true" className="h-3 w-3" /> Archived</span>}
              </div>
              <p className="mt-1.5 max-w-3xl break-words text-sm leading-6 text-muted">{board.description || 'No description yet.'}</p>
            </div>
          </div>
          <div className="flex shrink-0 gap-2">
            {canManage && <Button variant="secondary" disabled={changesPending} onClick={() => setSettingsOpen(true)}><Settings2 className="h-4 w-4" /> Settings</Button>}
            {canEdit && <Button disabled={changesPending} onClick={openCreate}><Plus className="h-4 w-4" /> New task</Button>}
          </div>
        </div>
      </header>

      {board.isArchived && <Alert tone="info" className="mt-6">This board is archived and read-only. An owner or admin can restore it from Board settings.</Alert>}
      {error && <Alert className="mt-6">{error}</Alert>}
      {needsRefresh && <Button className="mt-3" variant="secondary" onClick={() => void loadBoard()}>Reload board</Button>}

      <div className="mt-7 grid gap-3">
        <RequestStatusNotice status={detailStatus.stats} label="Statistics" hasPreviousData={stats !== null} onRetry={() => void refreshDetails(['stats'])} />
        {(stats || detailStatus.stats === 'loading') && <BoardStatsBar result={stats} />}
        <RequestStatusNotice status={detailStatus.members} label="Member list" hasPreviousData={members.length > 0} onRetry={() => void refreshDetails(['members'])} />
      </div>

      <div className="mt-7 flex flex-col gap-3 lg:flex-row lg:items-center">
        <label className="relative min-w-0 flex-1">
          <span className="sr-only">Search tasks</span>
          <Search aria-hidden="true" className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-faint" />
          <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search tasks, descriptions, or assignees…" className="min-h-11 w-full rounded-xl border border-line bg-surface pl-10 pr-3 text-sm text-ink placeholder:text-faint transition focus:border-accent" />
        </label>
        <div className="flex min-w-0 items-center gap-2">
          <Filter aria-hidden="true" className="ml-1 hidden h-4 w-4 shrink-0 text-faint sm:block" />
          <label className="min-w-0 flex-1 lg:flex-none"><span className="sr-only">Filter by priority</span><select value={priority} onChange={(event) => setPriority(event.target.value as PriorityFilter)} className="min-h-11 w-full rounded-xl border border-line bg-surface px-3 text-xs font-medium text-muted"><option value="All">All priorities</option>{TASK_PRIORITIES.map((value) => <option key={value}>{value}</option>)}</select></label>
          <label className="min-w-0 flex-1 lg:flex-none"><span className="sr-only">Filter by assignee</span><select value={assignee} onChange={(event) => setAssignee(event.target.value)} className="min-h-11 w-full max-w-56 rounded-xl border border-line bg-surface px-3 text-xs font-medium text-muted"><option value="All">All assignees</option><option value="Unassigned">Unassigned</option>{assignees.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label>
        </div>
      </div>

      {(search || priority !== 'All' || assignee !== 'All') && <p role="status" className="mt-3 text-xs font-medium text-muted">Showing {visibleTasks.length} of {board.tasks.length} tasks</p>}

      <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
        <p className="text-xs text-muted">{canEdit ? 'Drag the grip to reorder tasks or move between columns. Cards make room as you drag.' : 'Tasks are read-only on this board.'}</p>
        <BoardDensitySwitch density={density} onChange={setDensity} />
      </div>

      <div className="mt-5 grid items-start gap-5 2xl:grid-cols-[minmax(0,1fr)_18rem]">
        <div className="min-w-0">
          <BoardDragDrop
            tasks={visibleTasks}
            canEdit={canEdit}
            density={density}
            pendingTaskIds={disabledTaskIds}
            isMovePending={() => pendingTaskIdsRef.current.size > 0 || needsRefresh}
            onCreate={openCreate}
            onEdit={openEdit}
            onMove={(task, state) => void move(task, state)}
            onReorder={(task, state, beforeTaskId, restoreFocus) => void reorder(task, state, beforeTaskId, restoreFocus)}
            onDelete={(task) => { setDeleteError(null); setDeleteCandidate(task) }}
          />
        </div>
        <ActivityPanel activity={activity} status={detailStatus.activity} onRetry={() => void refreshDetails(['activity'])} />
      </div>

      <TaskEditorDialog open={editorOpen} task={editingTask} members={members} membersStatus={detailStatus.members} onRetryMembers={() => void refreshDetails(['members'])} onClose={() => setEditorOpen(false)} onSave={saveTask} />
      <ConfirmDialog
        open={deleteCandidate !== null}
        title="Delete task?"
        description={`“${deleteCandidate?.title ?? 'This task'}” will be permanently removed. This cannot be undone.`}
        confirmLabel="Delete task"
        error={deleteError}
        isPending={isDeleting}
        onClose={() => { setDeleteCandidate(null); setDeleteError(null) }}
        onConfirm={() => void confirmDelete()}
      />
      {canManage && <BoardSettingsDialog board={board} open={settingsOpen} onClose={() => setSettingsOpen(false)} onSave={saveBoard} onArchive={archiveBoard} />}
    </div>
  )
}
