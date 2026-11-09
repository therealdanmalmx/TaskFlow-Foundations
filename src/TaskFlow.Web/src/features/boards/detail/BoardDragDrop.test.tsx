import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../../App'
import type { TaskItem } from '../../tasks/model'

type Registration = { id: string; disabled?: boolean; index: number; group: string }
type Point = { x: number; y: number }
type DragEvent = {
  canceled: boolean
  nativeEvent?: Event
  operation: {
    canceled: boolean
    source: Registration & {
      initialIndex: number
      initialGroup: string
      manager: { dragOperation: { shape: { current: { center: Point } }; position: { current: Point } } }
    }
    target: { id: string; shape: { center: Point } } | null
  }
  preventDefault: () => void
}
const dragBoundary = vi.hoisted(() => ({
  beforeStart: undefined as ((event: DragEvent) => void) | undefined,
  start: undefined as ((event: DragEvent) => void) | undefined,
  over: undefined as ((event: DragEvent) => void) | undefined,
  end: undefined as ((event: DragEvent) => void) | undefined,
  sortables: new Map<string, Registration>(),
  droppables: new Map<string, { disabled?: boolean }>(),
}))

// Keep the real ordering helper, board state and HTTP boundary. Only replace the
// sensors and supply geometry, leaving pointer hit testing to the browser checks.
vi.mock('@dnd-kit/react', () => ({
  DragDropProvider: ({ children, onBeforeDragStart, onDragStart, onDragOver, onDragEnd }: {
    children: ReactNode
    onBeforeDragStart: (event: DragEvent) => void
    onDragStart: (event: DragEvent) => void
    onDragOver: (event: DragEvent) => void
    onDragEnd: (event: DragEvent) => void
  }) => {
    dragBoundary.beforeStart = onBeforeDragStart
    dragBoundary.start = onDragStart
    dragBoundary.over = onDragOver
    dragBoundary.end = onDragEnd
    return children
  },
  DragOverlay: () => null,
  PointerSensor: class {},
  KeyboardSensor: { configure: () => ({}) },
  useDroppable: (input: { id: string; disabled?: boolean }) => {
    dragBoundary.droppables.set(input.id, input)
    return { ref: () => {}, isDropTarget: false, droppable: input }
  },
}))
vi.mock('@dnd-kit/react/sortable', () => ({
  useSortable: (input: Registration) => {
    dragBoundary.sortables.set(input.id, input)
    return { ref: () => {}, handleRef: () => {}, isDragging: false, isDropping: false, isDragSource: false, sortable: input }
  },
}))

const account = { id: '7c5ac093-803a-4d65-9ff8-604883410001', username: 'milan', displayName: 'Milan Jovanović', email: 'milan@example.com' }
const workspace = { id: '7c5ac093-803a-4d65-9ff8-604883410010', name: 'TaskFlow Team', slug: 'taskflow-team', role: 'Owner', createdAt: '2026-01-01T00:00:00Z' }
const task: TaskItem = {
  id: '7c5ac093-803a-4d65-9ff8-604883410100', boardId: '7c5ac093-803a-4d65-9ff8-604883410020', title: 'Ship the release', description: 'Verify the exact release.',
  status: 'Todo', priority: 'High', dueDate: null, position: 1,
  createdByUserId: account.id, createdByName: account.displayName, assigneeId: null, assigneeName: null,
  createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', completedAt: null,
}
const board = {
  id: task.boardId, workspaceId: workspace.id, name: 'Production launch', description: 'Release checklist',
  color: '#2563eb', role: 'Owner', isArchived: false, taskCount: 1, tasks: [task], updatedAt: task.updatedAt,
}
const stats = { todo: 1, doing: 0, done: 0, total: 1, doneToday: 0, oldestActiveAgeDays: 1, overdue: 0 }
const boardUrl = `/api/boards/${board.id}`
const moveUrl = `/api/tasks/${task.id}/status`
type Handler = (init?: RequestInit) => Response | Promise<Response>

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
function sibling(index: number, overrides: Partial<TaskItem> = {}): TaskItem {
  return { ...task, id: `7c5ac093-803a-4d65-9ff8-60488341010${index}`, title: `Task ${index}`, position: index, ...overrides }
}
function withTasks(tasks: TaskItem[]) {
  return { ...board, tasks, taskCount: tasks.length }
}
function stubApi(overrides: Record<string, Handler> = {}, initialBoard = board, savedBoard = initialBoard) {
  let currentBoard = initialBoard
  const routes: Record<string, Handler> = {
    'GET /api/users/me': () => json(account),
    'GET /api/workspaces': () => json([workspace]),
    [`GET /api/workspaces/${workspace.id}/boards`]: () => json([currentBoard]),
    [`GET ${boardUrl}`]: () => json(currentBoard),
    [`GET ${boardUrl}/stats`]: () => json(stats),
    [`GET ${boardUrl}/activity`]: () => json({ available: true, entries: [] }),
    [`GET /api/workspaces/${workspace.id}/members`]: () => json([]),
    [`PUT ${moveUrl}`]: () => json(savedBoard.tasks.find((item) => item.id === task.id)),
    ...overrides,
  }
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const key = `${init?.method ?? 'GET'} ${String(input)}`
    const handler = routes[key]
    if (!handler) throw new Error(`Unmocked request: ${key}`)
    const response = await handler(init)
    if (key === `PUT ${moveUrl}` && response.ok) currentBoard = savedBoard
    return response
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}
function moveRequests(fetchMock: ReturnType<typeof stubApi>) {
  return fetchMock.mock.calls.filter(([input, init]) => String(input) === moveUrl && init?.method === 'PUT')
}
function lastMove(fetchMock: ReturnType<typeof stubApi>) {
  return JSON.parse(String(moveRequests(fetchMock).at(-1)?.[1]?.body))
}

type DragOptions = { canceled?: boolean; sourceId?: string; keyboard?: boolean; below?: boolean }
let activeDrag: { id: string; initialIndex: number; initialGroup: string; keyboard: boolean } | null = null
function dragEvent(target: string | null, options: DragOptions = {}): DragEvent {
  const id = options.sourceId ?? activeDrag?.id ?? task.id
  const sortable = dragBoundary.sortables.get(id)
  const center = { x: 50, y: options.below ? 60 : 40 }
  const canceled = options.canceled ?? false
  return {
    canceled,
    nativeEvent: (options.keyboard ?? activeDrag?.keyboard) ? new KeyboardEvent('keydown', { key: ' ', code: 'Space' }) : undefined,
    operation: {
      canceled,
      source: {
        id, index: sortable?.index ?? 0, group: sortable?.group ?? 'Todo',
        initialIndex: activeDrag?.initialIndex ?? sortable?.index ?? 0,
        initialGroup: activeDrag?.initialGroup ?? sortable?.group ?? 'Todo',
        manager: { dragOperation: { shape: { current: { center } }, position: { current: center } } },
      },
      target: target ? { id: target, shape: { center: { x: 50, y: 50 } } } : null,
    },
    preventDefault: vi.fn(),
  }
}
function startDrag(options: DragOptions = {}) {
  const event = dragEvent(null, options)
  act(() => dragBoundary.beforeStart!(event))
  if (vi.mocked(event.preventDefault).mock.calls.length > 0) return false
  const { source } = event.operation
  activeDrag = { id: source.id, initialIndex: source.index, initialGroup: source.group, keyboard: options.keyboard ?? false }
  act(() => dragBoundary.start!(event))
  return true
}
function hover(target: string | null, options: DragOptions = {}) {
  act(() => dragBoundary.over!(dragEvent(target, options)))
}
function release(target: string | null, options: DragOptions = {}) {
  act(() => dragBoundary.end!(dragEvent(target, options)))
  activeDrag = null
}
function drop(target: string | null, options: DragOptions = {}) {
  if (!startDrag(options)) return
  hover(target, options)
  release(target, options)
}
function column(name: string) { return screen.getByRole('region', { name }) }
function taskTitles(state: string) {
  return within(column(state)).queryAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)
}
async function openBoard() {
  render(<App />)
  await screen.findByRole('heading', { name: board.name, level: 1 })
}
async function saved() {
  await waitFor(() => expect(screen.getByRole('button', { name: `Drag ${task.title}` })).toBeEnabled())
}

beforeEach(() => {
  window.history.replaceState({}, '', `/workspaces/${workspace.id}/boards/${board.id}`)
  localStorage.clear()
  activeDrag = null
  dragBoundary.beforeStart = dragBoundary.start = dragBoundary.over = dragBoundary.end = undefined
  dragBoundary.sortables.clear()
  dragBoundary.droppables.clear()
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  localStorage.clear()
  window.history.replaceState({}, '', '/')
})

describe('board drag and drop', () => {
  it('previews a member moving into an empty column before release, then reconciles the persisted board', async () => {
    const moved = { ...task, status: 'Doing' as const, description: 'The persisted description', position: 0, updatedAt: '2026-02-01T00:00:00Z' }
    const fetchMock = stubApi({}, { ...board, role: 'Member' }, { ...withTasks([moved]), role: 'Member' })
    await openBoard()
    startDrag()
    hover('Doing')
    expect(taskTitles('Doing')).toEqual([task.title])
    expect(taskTitles('Todo')).toEqual([])
    expect(moveRequests(fetchMock)).toHaveLength(0)
    release('Doing')
    await saved()
    expect(taskTitles('Doing')).toEqual([task.title])
    expect(lastMove(fetchMock)).toEqual({ status: 'Doing', placement: 'bottom' })
    expect(moveRequests(fetchMock)[0][1]).toMatchObject({ credentials: 'same-origin' })
    expect(fetchMock.mock.calls.filter(([input]) => input === boardUrl)).toHaveLength(2)
    await waitFor(() => {
      expect(fetchMock.mock.calls.filter(([input]) => input === `${boardUrl}/stats`)).toHaveLength(2)
      expect(fetchMock.mock.calls.filter(([input]) => input === `${boardUrl}/activity`)).toHaveLength(2)
    })
    await userEvent.click(screen.getByRole('button', { name: `Edit ${task.title}` }))
    expect(screen.getByRole('textbox', { name: 'Description' })).toHaveValue(moved.description)
  })

  it('keeps moving the preview between columns and restores everything when canceled', async () => {
    const existing = sibling(1, { status: 'Done', title: 'Add a DNS record' })
    const fetchMock = stubApi({}, withTasks([task, existing]))
    await openBoard()
    startDrag()
    hover('Doing')
    expect(taskTitles('Doing')).toEqual([task.title])
    hover(existing.id)
    expect(taskTitles('Doing')).toEqual([])
    expect(taskTitles('Done')).toEqual([task.title, existing.title])
    hover('Todo')
    expect(taskTitles('Todo')).toEqual([task.title])
    expect(taskTitles('Done')).toEqual([existing.title])
    hover('Doing')
    expect(taskTitles('Doing')).toEqual([task.title])
    expect(moveRequests(fetchMock)).toHaveLength(0)
    release('Doing', { canceled: true })
    expect(taskTitles('Todo')).toEqual([task.title])
    expect(taskTitles('Doing')).toEqual([])
    expect(taskTitles('Done')).toEqual([existing.title])
    expect(moveRequests(fetchMock)).toHaveLength(0)
  })

  it.each(['top', 'middle', 'end'] as const)('inserts at the %s of a populated column with a live preview', async (placement) => {
    const first = sibling(1, { status: 'Done', title: 'Create the server', position: 0 })
    const last = sibling(2, { status: 'Done', title: 'Verify the deployment', position: 1 })
    const moved = { ...task, status: 'Done' as const, completedAt: '2026-02-01T00:00:00Z' }
    const order = placement === 'top' ? [moved, first, last] : placement === 'middle' ? [first, moved, last] : [first, last, moved]
    const fetchMock = stubApi({}, withTasks([task, first, last]), withTasks(order.map((item, position) => ({ ...item, position }))))
    await openBoard()
    const target = placement === 'top' ? first.id : placement === 'middle' ? last.id : 'Done'
    const options = { below: placement === 'end' }
    startDrag()
    hover(target, options)
    expect(taskTitles('Done')).toEqual(order.map((item) => item.title))
    expect(taskTitles('Todo')).toEqual([])
    expect(moveRequests(fetchMock)).toHaveLength(0)
    release(target, options)
    await saved()
    expect(taskTitles('Done')).toEqual(order.map((item) => item.title))
    expect(lastMove(fetchMock)).toEqual(placement === 'end'
      ? { status: 'Done', placement: 'bottom' }
      : { status: 'Done', placement: 'before', beforeTaskId: placement === 'top' ? first.id : last.id })
  })

  it('uses the lower half of a card to preview insertion after it', async () => {
    const first = sibling(1, { status: 'Done', title: 'Create the server', position: 0 })
    const last = sibling(2, { status: 'Done', title: 'Verify the deployment', position: 1 })
    const persisted = [{ ...first, position: 0 }, { ...task, status: 'Done' as const, position: 1 }, { ...last, position: 2 }]
    const fetchMock = stubApi({}, withTasks([task, first, last]), withTasks(persisted))
    await openBoard()
    startDrag()
    hover(first.id, { below: true })
    expect(taskTitles('Done')).toEqual([first.title, task.title, last.title])
    release(first.id, { below: true })
    await saved()
    expect(lastMove(fetchMock)).toEqual({ status: 'Done', placement: 'before', beforeTaskId: last.id })
  })

  it.each(['earlier', 'later'] as const)('reorders within the same column to an %s position', async (direction) => {
    const first = sibling(1, { title: 'Add a DNS record', position: 0 })
    const last = sibling(2, { title: 'Verify the deployment', position: 2 })
    const order = direction === 'earlier' ? [task, first, last] : [first, last, task]
    const fetchMock = stubApi({}, withTasks([first, task, last]), withTasks(order.map((item, position) => ({ ...item, position }))))
    await openBoard()
    startDrag()
    hover(direction === 'earlier' ? first.id : last.id)
    expect(taskTitles('Todo')).toEqual(order.map((item) => item.title))
    expect(moveRequests(fetchMock)).toHaveLength(0)
    release(direction === 'earlier' ? first.id : last.id)
    await saved()
    expect(taskTitles('Todo')).toEqual(order.map((item) => item.title))
    expect(lastMove(fetchMock)).toEqual(direction === 'earlier'
      ? { status: 'Todo', placement: 'before', beforeTaskId: first.id }
      : { status: 'Todo', placement: 'bottom' })
  })

  it('anchors a filtered drop to the next visible task without discarding or reordering hidden tasks', async () => {
    const hidden = sibling(1, { status: 'Done', title: 'Add a DNS record', description: '', position: 0 })
    const first = sibling(2, { status: 'Done', title: 'Release: build the image', position: 1 })
    const last = sibling(3, { status: 'Done', title: 'Release: deploy the image', position: 2 })
    const persisted = [hidden, first, { ...task, status: 'Done' as const }, last].map((item, position) => ({ ...item, position }))
    const fetchMock = stubApi({}, withTasks([hidden, first, last, task]), withTasks(persisted))
    await openBoard()
    const search = screen.getByRole('textbox', { name: 'Search tasks' })
    await userEvent.type(search, 'release')
    expect(taskTitles('Done')).toEqual([first.title, last.title])
    startDrag()
    hover(last.id)
    expect(taskTitles('Done')).toEqual([first.title, task.title, last.title])
    release(last.id)
    await saved()
    expect(lastMove(fetchMock)).toEqual({ status: 'Done', placement: 'before', beforeTaskId: last.id })
    await userEvent.clear(search)
    expect(taskTitles('Done')).toEqual([hidden.title, first.title, task.title, last.title])
  })

  it.each([
    { name: 'canceled drag', target: 'Doing', options: { canceled: true } },
    { name: 'drop outside the board', target: null, options: {} },
    { name: 'unknown target', target: 'Elsewhere', options: {} },
  ])('restores the board after a $name, even after a preview moved the task', async ({ target, options }) => {
    const fetchMock = stubApi()
    await openBoard()
    startDrag()
    hover('Doing')
    expect(taskTitles('Doing')).toEqual([task.title])
    release(target, options)
    expect(moveRequests(fetchMock)).toHaveLength(0)
    expect(taskTitles('Todo')).toEqual([task.title])
    expect(taskTitles('Doing')).toEqual([])
    expect(screen.getByRole('button', { name: `Drag ${task.title}` })).toBeEnabled()
  })

  it('does not write when a task returns to its original visible position', async () => {
    const first = sibling(1, { position: 0 })
    const fetchMock = stubApi({}, withTasks([first, task]))
    await openBoard()
    startDrag()
    hover('Doing')
    hover('Todo', { below: true })
    expect(taskTitles('Todo')).toEqual([first.title, task.title])
    release('Todo', { below: true })
    expect(moveRequests(fetchMock)).toHaveLength(0)
    drop(task.id)
    expect(moveRequests(fetchMock)).toHaveLength(0)
  })

  it('rejects a task from another board at the drag boundary', async () => {
    const fetchMock = stubApi()
    await openBoard()
    expect(startDrag({ sourceId: '7c5ac093-803a-4d65-9ff8-604883410999' })).toBe(false)
    expect(moveRequests(fetchMock)).toHaveLength(0)
    expect(taskTitles('Todo')).toEqual([task.title])
  })

  it('keeps the optimistic placement and blocks new drags until save and board refresh finish', async () => {
    let finishMove!: (response: Response) => void
    let finishRefresh!: (response: Response) => void
    const pendingMove = new Promise<Response>((resolve) => { finishMove = resolve })
    const pendingRefresh = new Promise<Response>((resolve) => { finishRefresh = resolve })
    const other = sibling(1, { title: 'Add a DNS record' })
    const moved = { ...task, status: 'Doing' as const, position: 0 }
    const initial = withTasks([task, other])
    const persisted = withTasks([moved, other])
    let boardReads = 0
    const fetchMock = stubApi({
      [`PUT ${moveUrl}`]: () => pendingMove,
      [`GET ${boardUrl}`]: () => ++boardReads === 1 ? json(initial) : pendingRefresh,
    }, initial, persisted)
    await openBoard()
    drop('Doing')
    expect(taskTitles('Doing')).toEqual([task.title])
    expect(screen.getByRole('button', { name: `Drag ${task.title}` })).toBeDisabled()
    expect(startDrag()).toBe(false)
    expect(startDrag({ sourceId: other.id })).toBe(false)
    release('Done')
    expect(moveRequests(fetchMock)).toHaveLength(1)
    await act(async () => finishMove(json(moved)))
    expect(boardReads).toBe(2)
    expect(taskTitles('Doing')).toEqual([task.title])
    expect(screen.getByRole('button', { name: `Drag ${task.title}` })).toBeDisabled()
    expect(startDrag({ sourceId: other.id })).toBe(false)
    await act(async () => finishRefresh(json(persisted)))
    expect(screen.getByRole('button', { name: `Drag ${task.title}` })).toBeEnabled()
    expect(moveRequests(fetchMock)).toHaveLength(1)
  })

  it('rolls back a rejected move and allows a retry', async () => {
    let attempts = 0
    const first = sibling(1, { position: 0 })
    const persisted = withTasks([{ ...task, position: 0 }, { ...first, position: 1 }])
    const fetchMock = stubApi({
      [`PUT ${moveUrl}`]: () => ++attempts === 1 ? json({ error: 'The task could not be moved.' }, 503) : json(persisted.tasks[0]),
    }, withTasks([first, task]), persisted)
    await openBoard()
    drop(first.id)
    expect(await screen.findByText('The task could not be moved.')).toBeVisible()
    expect(taskTitles('Todo')).toEqual([first.title, task.title])
    expect(screen.getByRole('button', { name: `Drag ${task.title}` })).toBeEnabled()
    drop(first.id)
    await saved()
    expect(taskTitles('Todo')).toEqual([task.title, first.title])
    expect(screen.queryByText('The task could not be moved.')).not.toBeInTheDocument()
    expect(moveRequests(fetchMock)).toHaveLength(2)
  })

  it('keeps a saved move when refresh fails and requires a reload before another move', async () => {
    let boardReads = 0
    const moved = { ...task, status: 'Done' as const, position: 0 }
    const persisted = withTasks([moved])
    const fetchMock = stubApi({
      [`GET ${boardUrl}`]: () => ++boardReads === 1 ? json(board) : boardReads === 2 ? json({ error: 'Board refresh unavailable.' }, 503) : json(persisted),
    }, board, persisted)
    await openBoard()
    drop('Done')
    expect(await screen.findByText(/Task moved, but the board could not be refreshed/)).toBeVisible()
    expect(taskTitles('Done')).toEqual([task.title])
    expect(taskTitles('Todo')).toEqual([])
    expect(startDrag()).toBe(false)
    expect(moveRequests(fetchMock)).toHaveLength(1)
    await userEvent.click(screen.getByRole('button', { name: 'Reload board' }))
    await screen.findByRole('heading', { name: board.name, level: 1 })
    expect(taskTitles('Done')).toEqual([task.title])
    expect(screen.getByRole('button', { name: `Drag ${task.title}` })).toBeEnabled()
  })

  it.each([true, false])('restores keyboard focus after a delayed move (success: %s)', async (success) => {
    let finish!: (response: Response) => void
    const pending = new Promise<Response>((resolve) => { finish = resolve })
    const moved = { ...task, status: 'Doing' as const }
    stubApi({ [`PUT ${moveUrl}`]: () => pending }, board, withTasks([moved]))
    await openBoard()
    screen.getByRole('button', { name: `Drag ${task.title}` }).focus()
    drop('Doing', { keyboard: true })
    expect(within(column('Doing')).getByRole('button', { name: `Drag ${task.title}` })).toBeDisabled()
    document.body.tabIndex = -1
    document.body.focus()
    document.body.removeAttribute('tabindex')
    await act(async () => finish(success ? json(moved) : json({ error: 'The move failed.' }, 503)))
    const handle = within(column(success ? 'Doing' : 'Todo')).getByRole('button', { name: `Drag ${task.title}` })
    expect(handle).toBeEnabled()
    expect(handle).toHaveFocus()
  })

  it('restores keyboard focus to the original column when a preview is canceled', async () => {
    const fetchMock = stubApi()
    await openBoard()
    screen.getByRole('button', { name: `Drag ${task.title}` }).focus()
    startDrag({ keyboard: true })
    hover('Doing')
    expect(taskTitles('Doing')).toEqual([task.title])
    document.body.tabIndex = -1
    document.body.focus()
    document.body.removeAttribute('tabindex')

    release('Doing', { canceled: true })

    expect(within(column('Todo')).getByRole('button', { name: `Drag ${task.title}` })).toHaveFocus()
    expect(moveRequests(fetchMock)).toHaveLength(0)
  })

  it('preserves focus in search when the user leaves a pending keyboard move', async () => {
    let finish!: (response: Response) => void
    const pending = new Promise<Response>((resolve) => { finish = resolve })
    const moved = { ...task, status: 'Doing' as const }
    stubApi({ [`PUT ${moveUrl}`]: () => pending }, board, withTasks([moved]))
    await openBoard()
    screen.getByRole('button', { name: `Drag ${task.title}` }).focus()
    drop('Doing', { keyboard: true })
    const search = screen.getByRole('textbox', { name: 'Search tasks' })
    await userEvent.click(search)
    await act(async () => finish(json(moved)))
    expect(within(column('Doing')).getByRole('button', { name: `Drag ${task.title}` })).toBeEnabled()
    expect(search).toHaveFocus()
  })

  it('makes archived boards read-only at the sortable controls and event boundary', async () => {
    const fetchMock = stubApi({}, { ...board, isArchived: true })
    await openBoard()
    expect(screen.queryByRole('button', { name: `Drag ${task.title}` })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: `Move ${task.title} to Doing` })).not.toBeInTheDocument()
    expect(dragBoundary.sortables.get(task.id)?.disabled).toBe(true)
    for (const state of ['Todo', 'Doing', 'Done']) expect(dragBoundary.droppables.get(state)?.disabled).toBe(true)
    expect(startDrag()).toBe(false)
    expect(moveRequests(fetchMock)).toHaveLength(0)
    expect(taskTitles('Todo')).toEqual([task.title])
  })

  it('does not apply a delayed save or refresh metadata after navigating to another board', async () => {
    let finish!: (response: Response) => void
    const pending = new Promise<Response>((resolve) => { finish = resolve })
    const nextBoard = { ...board, id: '7c5ac093-803a-4d65-9ff8-604883410021', name: 'Next project', tasks: [], taskCount: 0 }
    const fetchMock = stubApi({
      [`PUT ${moveUrl}`]: () => pending,
      [`GET /api/workspaces/${workspace.id}/boards`]: () => json([board, nextBoard]),
      [`GET /api/boards/${nextBoard.id}`]: () => json(nextBoard),
      [`GET /api/boards/${nextBoard.id}/stats`]: () => json({ ...stats, todo: 0, total: 0 }),
      [`GET /api/boards/${nextBoard.id}/activity`]: () => json({ available: true, entries: [] }),
    })
    await openBoard()
    drop('Doing')
    await userEvent.click(screen.getByRole('link', { name: 'All boards' }))
    await userEvent.click(await screen.findByRole('link', { name: /Next project/ }))
    expect(await screen.findByRole('heading', { name: 'Next project', level: 1 })).toBeVisible()
    await act(async () => finish(json({ ...task, status: 'Doing' })))
    expect(screen.queryByRole('heading', { name: task.title })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Next project', level: 1 })).toBeVisible()
    expect(fetchMock.mock.calls.filter(([input]) => input === `${boardUrl}/stats`)).toHaveLength(1)
    expect(fetchMock.mock.calls.filter(([input]) => input === boardUrl)).toHaveLength(1)
    expect(moveRequests(fetchMock)).toHaveLength(1)
  })

  it('does not replace a newly opened board with a delayed ordering refresh', async () => {
    let finishRefresh!: (response: Response) => void
    const pendingRefresh = new Promise<Response>((resolve) => { finishRefresh = resolve })
    const moved = { ...task, status: 'Doing' as const }
    const persisted = withTasks([moved])
    const nextBoard = { ...board, id: '7c5ac093-803a-4d65-9ff8-604883410021', name: 'Next project', tasks: [], taskCount: 0 }
    let boardReads = 0
    const fetchMock = stubApi({
      [`GET ${boardUrl}`]: () => ++boardReads === 1 ? json(board) : pendingRefresh,
      [`GET /api/workspaces/${workspace.id}/boards`]: () => json([board, nextBoard]),
      [`GET /api/boards/${nextBoard.id}`]: () => json(nextBoard),
      [`GET /api/boards/${nextBoard.id}/stats`]: () => json({ ...stats, todo: 0, total: 0 }),
      [`GET /api/boards/${nextBoard.id}/activity`]: () => json({ available: true, entries: [] }),
    }, board, persisted)
    await openBoard()
    drop('Doing')
    await waitFor(() => expect(boardReads).toBe(2))

    await userEvent.click(screen.getByRole('link', { name: 'All boards' }))
    await userEvent.click(await screen.findByRole('link', { name: /Next project/ }))
    expect(await screen.findByRole('heading', { name: 'Next project', level: 1 })).toBeVisible()
    await act(async () => finishRefresh(json(persisted)))

    expect(screen.queryByRole('heading', { name: task.title })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Next project', level: 1 })).toBeVisible()
    expect(fetchMock.mock.calls.filter(([input]) => input === `${boardUrl}/stats`)).toHaveLength(1)
    expect(moveRequests(fetchMock)).toHaveLength(1)
  })
})
