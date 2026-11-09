import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from '../../App'
import {
  setupAppTests,
  activity,
  board,
  boardSummary,
  deferred,
  json,
  member,
  routeKey,
  secondBoard,
  secondWorkspace,
  stats,
  stubApi,
  task,
  teammate,
  visit,
  workspace,
} from '../../test-support/appFixture'

setupAppTests()

describe('TaskFlow boards', () => {
  it('creates a board from the dashboard and follows it', async () => {
    const newBoard = { ...boardSummary, id: '7c5ac093-803a-4d65-9ff8-604883410021', name: 'Customer onboarding', taskCount: 0 }
    stubApi({
      'POST /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards': () => json(newBoard, 201),
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410021': () => json({ ...newBoard, role: 'Owner', tasks: [] }),
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410021/stats': () => json({ ...stats, todo: 0, total: 0 }, 200, { 'X-Cache': 'MISS' }),
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410021/activity': () => json({ available: true, entries: [] }),
    })
    render(<App />)
    const actor = userEvent.setup()

    await actor.click(await screen.findByRole('button', { name: 'New board' }))
    await actor.type(screen.getByRole('textbox', { name: 'Board name' }), newBoard.name)
    await actor.type(screen.getByLabelText('Description'), 'A clear onboarding path.')
    await actor.click(screen.getByRole('button', { name: 'Create board' }))

    expect(await screen.findByRole('heading', { name: newBoard.name, level: 1 })).toBeVisible()
    expect(screen.getByText('cache MISS')).toBeVisible()
  })

  it('keeps the board usable and retries failed details independently', async () => {
    visit('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards/7c5ac093-803a-4d65-9ff8-604883410020')
    let statsRequests = 0
    let activityRequests = 0
    let memberRequests = 0
    const fetchMock = stubApi({
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410020/stats': () => ++statsRequests === 1 ? json({}, 503) : json(stats, 200, { 'X-Cache': 'HIT' }),
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410020/activity': () => ++activityRequests === 1 ? json({}, 503) : json({ available: false, entries: [] }),
      'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members': () => ++memberRequests === 1 ? json({}, 503) : json([member, teammate]),
    })
    render(<App />)
    const actor = userEvent.setup()

    expect(await screen.findByText(task.title)).toBeVisible()
    expect(screen.getByText('Statistics could not be loaded.')).toBeVisible()
    expect(screen.getByText('Activity could not be loaded.')).toBeVisible()
    expect(screen.getByText('Member list could not be loaded.')).toBeVisible()
    expect(screen.queryByLabelText('Loading board statistics')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Loading activity')).not.toBeInTheDocument()

    await actor.click(screen.getByRole('button', { name: 'Retry statistics' }))
    expect(await screen.findByText('cache HIT')).toBeVisible()
    expect(activityRequests).toBe(1)
    expect(memberRequests).toBe(1)

    await actor.click(screen.getByRole('button', { name: 'Retry activity' }))
    expect(await screen.findByText(/Activity is temporarily offline because the cache is unavailable/)).toBeVisible()
    expect(screen.queryByText('Activity could not be loaded.')).not.toBeInTheDocument()

    await actor.click(screen.getByRole('button', { name: 'Retry member list' }))
    await waitFor(() => expect(screen.queryByText('Member list could not be loaded.')).not.toBeInTheDocument())
    await actor.click(screen.getByRole('button', { name: `Edit ${task.title}` }))
    expect(screen.getByRole('combobox', { name: 'Assignee' })).toBeEnabled()
    expect(screen.getByRole('option', { name: teammate.displayName })).toBeVisible()
    expect(fetchMock.mock.calls.filter(([input, init]) => routeKey(input, init) === 'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410020')).toHaveLength(1)
    expect([statsRequests, activityRequests, memberRequests]).toEqual([2, 2, 2])
  })

  it('marks retained statistics and activity as stale after a refresh fails', async () => {
    visit('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards/7c5ac093-803a-4d65-9ff8-604883410020')
    let statsRequests = 0
    let activityRequests = 0
    const fetchMock = stubApi({
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410020/stats': () => ++statsRequests === 2 ? json({}, 503) : json(statsRequests > 2 ? { ...stats, doing: 1 } : stats, 200, { 'X-Cache': 'MISS' }),
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410020/activity': () => ++activityRequests === 2 ? json({}, 503) : json(activityRequests > 2 ? { available: true, entries: [{ message: 'Moved the release task', at: new Date().toISOString() }] } : activity),
      'PUT /api/tasks/7c5ac093-803a-4d65-9ff8-604883410100/status': () => json({ ...task, status: 'Doing' }),
    })
    render(<App />)
    const actor = userEvent.setup()

    expect(await screen.findByText(activity.entries[0].message)).toBeVisible()
    await actor.click(screen.getByRole('button', { name: `Move ${task.title} to Doing` }))
    expect(await screen.findByText(/Statistics could not be loaded. Showing the last successful result./)).toBeVisible()
    expect(screen.getByText(/Activity could not be loaded. Showing the last successful result./)).toBeVisible()
    expect(screen.getByText(activity.entries[0].message)).toBeVisible()
    expect(screen.getByText('cache MISS')).toBeVisible()
    const doingColumn = screen.getByRole('heading', { name: 'Doing' }).closest('section')!
    expect(within(doingColumn).getByText(task.title)).toBeVisible()

    await actor.click(screen.getByRole('button', { name: 'Retry statistics' }))
    await waitFor(() => expect(screen.queryByText(/Statistics could not be loaded/)).not.toBeInTheDocument())
    expect(screen.getByText('In progress').previousElementSibling).toHaveTextContent('1')
    await actor.click(screen.getByRole('button', { name: 'Retry activity' }))
    expect(await screen.findByText('Moved the release task')).toBeVisible()
    expect(screen.queryByText(/Showing the last successful result/)).not.toBeInTheDocument()
    expect(fetchMock.mock.calls.filter(([input, init]) => routeKey(input, init) === 'PUT /api/tasks/7c5ac093-803a-4d65-9ff8-604883410100/status')).toHaveLength(1)
  })

  it('ignores a stale board response after route parameters change', async () => {
    visit('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards/7c5ac093-803a-4d65-9ff8-604883410020')
    const firstBoard = deferred<Response>()
    const fetchMock = stubApi({
      'GET /api/workspaces': () => json([workspace, secondWorkspace]),
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410020': () => firstBoard.promise,
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410021': () => json(secondBoard),
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410021/stats': () => json({ ...stats, todo: 0, total: 0 }, 200, { 'X-Cache': 'MISS' }),
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410021/activity': () => json({ available: true, entries: [] }),
      'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410011/members': () => json([]),
    })
    render(<App />)

    await waitFor(() => expect(fetchMock.mock.calls.some(([input, init]) => routeKey(input, init) === 'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410020')).toBe(true))
    window.history.pushState({}, '', '/workspaces/7c5ac093-803a-4d65-9ff8-604883410011/boards/7c5ac093-803a-4d65-9ff8-604883410021')
    window.dispatchEvent(new PopStateEvent('popstate'))

    expect(await screen.findByRole('heading', { name: secondBoard.name })).toBeVisible()
    firstBoard.resolve(json(board))
    await waitFor(() => expect(screen.queryByRole('heading', { name: board.name })).not.toBeInTheDocument())
    expect(fetchMock.mock.calls.some(([input, init]) => routeKey(input, init) === 'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410011/members')).toBe(true)
    expect(fetchMock.mock.calls.some(([input, init]) => routeKey(input, init) === 'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members')).toBe(false)
  })

  it('redirects a board URL to its canonical workspace before loading context', async () => {
    visit('/workspaces/7c5ac093-803a-4d65-9ff8-604883410999/boards/7c5ac093-803a-4d65-9ff8-604883410020')
    const fetchMock = stubApi()
    render(<App />)

    expect(await screen.findByRole('heading', { name: board.name })).toBeVisible()
    expect(window.location.pathname).toBe('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards/7c5ac093-803a-4d65-9ff8-604883410020')
    expect(screen.getByRole('link', { name: 'All boards' })).toHaveAttribute('href', '/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards')
    expect(fetchMock.mock.calls.some(([input, init]) => routeKey(input, init) === 'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410999/members')).toBe(false)
    expect(fetchMock.mock.calls.some(([input, init]) => routeKey(input, init) === 'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members')).toBe(true)
  })

  it('closes board-scoped dialogs when navigating to another board', async () => {
    visit('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards/7c5ac093-803a-4d65-9ff8-604883410020')
    stubApi({
      'GET /api/workspaces': () => json([workspace, secondWorkspace]),
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410021': () => json(secondBoard),
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410021/stats': () => json({ ...stats, todo: 0, total: 0 }),
      'GET /api/boards/7c5ac093-803a-4d65-9ff8-604883410021/activity': () => json({ available: true, entries: [] }),
      'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410011/members': () => json([]),
    })
    render(<App />)
    const actor = userEvent.setup()

    await actor.click(await screen.findByRole('button', { name: `Edit ${task.title}` }))
    expect(screen.getByRole('dialog', { name: 'Edit task' })).toBeVisible()
    window.history.pushState({}, '', '/workspaces/7c5ac093-803a-4d65-9ff8-604883410011/boards/7c5ac093-803a-4d65-9ff8-604883410021')
    window.dispatchEvent(new PopStateEvent('popstate'))

    expect(await screen.findByRole('heading', { name: secondBoard.name })).toBeVisible()
    expect(screen.queryByRole('dialog', { name: 'Edit task' })).not.toBeInTheDocument()
  })
})

