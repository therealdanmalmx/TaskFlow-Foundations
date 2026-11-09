import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach, vi } from 'vitest'

export const user = {
  id: '7c5ac093-803a-4d65-9ff8-604883410001',
  username: 'milan',
  email: 'milan@example.com',
  displayName: 'Milan Jovanović',
}

export const workspace = {
  id: '7c5ac093-803a-4d65-9ff8-604883410010',
  name: 'TaskFlow Team',
  slug: 'taskflow-team',
  role: 'Owner',
  createdAt: '2026-01-01T00:00:00Z',
}

export const secondWorkspace = {
  ...workspace,
  id: '7c5ac093-803a-4d65-9ff8-604883410011',
  name: 'Learning Lab',
  slug: 'learning-lab',
}

export const personalWorkspace = {
  ...workspace,
  name: `${user.displayName}'s Workspace`,
  slug: user.username,
}

export const task = {
  id: '7c5ac093-803a-4d65-9ff8-604883410100',
  boardId: '7c5ac093-803a-4d65-9ff8-604883410020',
  title: 'Ship the release',
  description: 'Prove the exact image is live.',
  status: 'Todo',
  priority: 'High',
  dueDate: '2099-08-31',
  position: 1,
  createdByUserId: '7c5ac093-803a-4d65-9ff8-604883410001',
  createdByName: 'Milan Jovanović',
  assigneeId: '7c5ac093-803a-4d65-9ff8-604883410001',
  assigneeName: 'Milan Jovanović',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  completedAt: null,
}

export const boardSummary = {
  id: '7c5ac093-803a-4d65-9ff8-604883410020',
  workspaceId: '7c5ac093-803a-4d65-9ff8-604883410010',
  name: 'Production launch',
  description: 'Everything required for a safe release.',
  color: '#2563eb',
  isArchived: false,
  taskCount: 1,
  updatedAt: '2026-01-01T00:00:00Z',
}

export const board = { ...boardSummary, role: 'Owner', tasks: [task] }
const secondBoardSummary = { ...boardSummary, id: '7c5ac093-803a-4d65-9ff8-604883410021', workspaceId: '7c5ac093-803a-4d65-9ff8-604883410011', name: 'Curriculum launch', taskCount: 0 }
export const secondBoard = { ...secondBoardSummary, role: 'Owner', tasks: [] }
export const personalBoardSummary = {
  ...boardSummary,
  name: 'My tasks',
  description: 'A place to plan what comes next.',
  taskCount: 0,
}
export const stats = { todo: 1, doing: 0, done: 0, total: 1, doneToday: 0, oldestActiveAgeDays: 2.5, overdue: 0 }
export const activity = { available: true, entries: [{ message: 'Created “Ship the release”', at: new Date().toISOString() }] }
export const member = { userId: '7c5ac093-803a-4d65-9ff8-604883410001', username: user.username, email: user.email, displayName: user.displayName, role: 'Owner', joinedAt: '2026-01-01T00:00:00Z' }
export const teammate = { userId: '7c5ac093-803a-4d65-9ff8-604883410002', username: 'alex', email: 'alex@example.com', displayName: 'Alex Morgan', role: 'Member', joinedAt: '2026-02-01T00:00:00Z' }

type RouteHandler = (init?: RequestInit) => Response | Promise<Response>
type Routes = Record<string, RouteHandler>

export function json(body: unknown, status = 200, headers: Record<string, string> = {}) {
  return new Response(status === 204 ? null : JSON.stringify(body), {
    status,
    headers: status === 204 ? headers : { 'Content-Type': 'application/json', ...headers },
  })
}

export function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((complete) => { resolve = complete })
  return { promise, resolve }
}

export function routeKey(input: RequestInfo | URL, init?: RequestInit): string {
  return `${init?.method ?? 'GET'} ${String(input)}`
}

export function stubApi(overrides: Routes = {}) {
  const routes: Routes = {
    'GET /api/users/me': () => json(user),
    'GET /api/workspaces': () => json([workspace]),
    'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards': () => json([boardSummary]),
    'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410020': () => json(board),
    'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410020/stats': () => json(stats, 200, { 'X-Cache': 'HIT' }),
    'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410020/activity': () => json(activity),
    'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members': () => json([member]),
    'POST /auth/logout': () => json(undefined, 204),
    ...overrides,
  }

  const mock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const key = routeKey(input, init)
    const handler = routes[key]
    if (!handler) throw new Error(`Unmocked fetch: ${key}`)
    return Promise.resolve(handler(init))
  })
  vi.stubGlobal('fetch', mock)
  return mock
}

export function visit(path: string) {
  window.history.replaceState({}, '', path)
}

export function setupAppTests() {
  beforeEach(() => visit('/'))

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    window.history.replaceState({}, '', '/')
  })
}

