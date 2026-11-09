import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from '../../App'
import {
  setupAppTests,
  board,
  json,
  member,
  routeKey,
  stubApi,
  task,
  teammate,
  user,
  visit,
  workspace,
} from '../../test-support/appFixture'

setupAppTests()

describe('TaskFlow tasks', () => {
  it('moves and deletes a task with explicit confirmation', async () => {
    visit('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards/7c5ac093-803a-4d65-9ff8-604883410020')
    const movedTask = { ...task, status: 'Doing', updatedAt: '2026-01-02T00:00:00Z' }
    stubApi({
      'PUT /api/tasks/7c5ac093-803a-4d65-9ff8-604883410100/status': () => json(movedTask),
      'DELETE /api/tasks/7c5ac093-803a-4d65-9ff8-604883410100': () => json(undefined, 204),
    })
    render(<App />)
    const actor = userEvent.setup()

    expect(await screen.findByText(task.title)).toBeVisible()
    await actor.click(screen.getByRole('button', { name: `Move ${task.title} to Doing` }))
    const doingColumn = screen.getByRole('heading', { name: 'Doing' }).closest('section')
    expect(doingColumn).not.toBeNull()
    expect(await within(doingColumn!).findByText(task.title)).toBeVisible()

    await actor.click(screen.getByRole('button', { name: `Delete ${task.title}` }))
    expect(screen.getByRole('dialog', { name: 'Delete task?' })).toBeVisible()
    await actor.click(screen.getByRole('button', { name: 'Delete task' }))
    await waitFor(() => expect(screen.queryByText(task.title)).not.toBeInTheDocument())
  })

  it('preserves an assignment when member lookup fails and retries within the editor', async () => {
    visit('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards/7c5ac093-803a-4d65-9ff8-604883410020')
    let memberRequests = 0
    const fetchMock = stubApi({
      'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members': () => ++memberRequests === 1 ? json({}, 503) : json([member, teammate]),
      'PUT /api/tasks/7c5ac093-803a-4d65-9ff8-604883410100': (init) => json({ ...task, ...JSON.parse(String(init?.body)) }),
    })
    render(<App />)
    const actor = userEvent.setup()

    await actor.click(await screen.findByRole('button', { name: `Edit ${task.title}` }))
    expect(screen.getByRole('combobox', { name: 'Assignee' })).toBeDisabled()
    expect(screen.getByRole('combobox', { name: 'Assignee' })).toHaveValue(user.id)
    await actor.type(screen.getByRole('textbox', { name: 'Title' }), ' today')
    await actor.click(screen.getByRole('button', { name: 'Save task' }))
    expect(await screen.findByText(`${task.title} today`)).toBeVisible()
    const updateCall = fetchMock.mock.calls.find(([input, init]) => routeKey(input, init) === 'PUT /api/tasks/7c5ac093-803a-4d65-9ff8-604883410100')
    expect(JSON.parse(String(updateCall?.[1]?.body)).assigneeId).toBe(user.id)

    await actor.click(screen.getByRole('button', { name: `Edit ${task.title} today` }))
    const editor = screen.getByRole('dialog', { name: 'Edit task' })
    await actor.click(within(editor).getByRole('button', { name: 'Retry member list' }))
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Assignee' })).toBeEnabled())
    expect(screen.getByRole('option', { name: teammate.displayName })).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'Title' })).toHaveValue(`${task.title} today`)
  })

  it('filters and updates task assignments using UUIDs and preserves the unassigned option', async () => {
    visit(`/workspaces/${workspace.id}/boards/${board.id}`)
    const fetchMock = stubApi({
      [`GET /api/workspaces/${workspace.id}/members`]: () => json([member, teammate]),
      [`PUT /api/tasks/${task.id}`]: (init) => {
        const input = JSON.parse(String(init?.body))
        return json({ ...task, ...input, assigneeName: input.assigneeId ? teammate.displayName : null })
      },
    })
    render(<App />)
    const actor = userEvent.setup()

    expect(await screen.findByText(task.title)).toBeVisible()
    await actor.selectOptions(screen.getByRole('combobox', { name: 'Filter by assignee' }), user.id)
    expect(screen.getByText('Showing 1 of 1 tasks')).toBeVisible()
    await actor.selectOptions(screen.getByRole('combobox', { name: 'Filter by assignee' }), 'All')

    await actor.click(screen.getByRole('button', { name: `Edit ${task.title}` }))
    await actor.selectOptions(screen.getByRole('combobox', { name: 'Assignee' }), teammate.userId)
    await actor.click(screen.getByRole('button', { name: 'Save task' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await actor.selectOptions(screen.getByRole('combobox', { name: 'Filter by assignee' }), teammate.userId)
    expect(screen.getByText('Showing 1 of 1 tasks')).toBeVisible()
    await actor.selectOptions(screen.getByRole('combobox', { name: 'Filter by assignee' }), 'All')

    await actor.click(screen.getByRole('button', { name: `Edit ${task.title}` }))
    await actor.selectOptions(screen.getByRole('combobox', { name: 'Assignee' }), '')
    await actor.click(screen.getByRole('button', { name: 'Save task' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await actor.selectOptions(screen.getByRole('combobox', { name: 'Filter by assignee' }), 'Unassigned')
    expect(screen.getByText('Showing 1 of 1 tasks')).toBeVisible()

    const updates = fetchMock.mock.calls.filter(([input, init]) => routeKey(input, init) === `PUT /api/tasks/${task.id}`)
    expect(updates.map(([, init]) => JSON.parse(String(init?.body)).assigneeId)).toEqual([teammate.userId, null])
  })
})

