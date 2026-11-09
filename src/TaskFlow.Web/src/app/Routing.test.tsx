import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from '../App'
import {
  setupAppTests,
  board,
  stubApi,
  task,
  visit,
  workspace,
} from '../test-support/appFixture'

setupAppTests()

describe('TaskFlow routing', () => {
  it.each([
    '/workspaces/not-a-uuid/members',
    '/workspaces/10/members',
    '/workspaces/10/boards',
    `/workspaces/${workspace.id}/boards/20`,
    `/workspaces/not-a-uuid/boards/${board.id}`,
    `/workspaces/${workspace.id}-invalid/boards`,
  ])('renders invalid identifier route %s as not found without issuing invalid API calls', async (route) => {
    visit(route)
    const fetchMock = stubApi()
    render(<App />)

    expect(await screen.findByText('Page not found')).toBeVisible()
    expect(fetchMock.mock.calls.some(([input]) => /\/api\/(workspaces|boards)\//.test(String(input)))).toBe(false)
  })

  it('loads uppercase UUID routes using the canonical identifiers returned by the API', async () => {
    visit(`/workspaces/${workspace.id.toUpperCase()}/boards/${board.id.toUpperCase()}`)
    const fetchMock = stubApi()
    render(<App />)

    expect(await screen.findByRole('heading', { name: board.name, level: 1 })).toBeVisible()
    expect(await screen.findByRole('heading', { name: task.title })).toBeVisible()
    expect(screen.getByRole('link', { name: 'All boards' })).toHaveAttribute('href', `/workspaces/${workspace.id}/boards`)
    expect(fetchMock.mock.calls.some(([input]) => input === `/api/workspaces/${workspace.id}/members`)).toBe(true)
  })
})
