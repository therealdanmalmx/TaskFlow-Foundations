import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from '../../App'
import {
  setupAppTests,
  json,
  member,
  stubApi,
  teammate,
  user,
  visit,
  workspace,
} from '../../test-support/appFixture'

setupAppTests()

describe('TaskFlow members', () => {
  it('keeps member management restricted for regular members', async () => {
    visit('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members')
    stubApi({
      'GET /api/workspaces': () => json([{ ...workspace, role: 'Member' }]),
      'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members': () => json([{ ...member, role: 'Member' }]),
    })
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Members' })).toBeVisible()
    expect(screen.getByText(/Only owners and admins/)).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Add member' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: `Leave ${workspace.name}` })).toBeVisible()
    expect(screen.queryByRole('option', { name: 'Owner' })).not.toBeInTheDocument()
    expect(screen.getByText(user.email, { exact: false })).toBeVisible()
  })

  it('lets a regular member leave and refreshes workspace navigation', async () => {
    visit('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members')
    let workspaceRequest = 0
    const memberWorkspace = { ...workspace, role: 'Member' }
    stubApi({
      'GET /api/workspaces': () => json(workspaceRequest++ === 0 ? [memberWorkspace] : []),
      'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members': () => json([{ ...member, role: 'Member' }]),
      'DELETE /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members/7c5ac093-803a-4d65-9ff8-604883410001': () => json(undefined, 204),
    })
    render(<App />)
    const actor = userEvent.setup()

    await actor.click(await screen.findByRole('button', { name: `Leave ${workspace.name}` }))
    expect(screen.getByRole('dialog', { name: 'Leave workspace?' })).toBeVisible()
    await actor.click(screen.getByRole('button', { name: 'Leave workspace' }))

    expect(await screen.findByRole('heading', { name: 'Create your first workspace' })).toBeVisible()
    expect(window.location.pathname).toBe('/workspaces/new')
  })

  it('offers ownership only to owners and lets an owner manage another owner', async () => {
    visit('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members')
    stubApi({
      'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members': () => json([member, { ...teammate, role: 'Owner' }]),
    })
    const { unmount } = render(<App />)

    const ownerRole = await screen.findByLabelText(`Role for ${teammate.displayName}`)
    expect(ownerRole).toBeEnabled()
    expect(within(ownerRole).getByRole('option', { name: 'Owner' })).toBeVisible()
    expect(screen.getByRole('button', { name: `Remove ${teammate.displayName}` })).toBeEnabled()

    unmount()
    visit('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members')
    stubApi({
      'GET /api/workspaces': () => json([{ ...workspace, role: 'Admin' }]),
      'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/members': () => json([{ ...member, role: 'Admin' }, teammate]),
    })
    render(<App />)

    const adminRole = await screen.findByLabelText(`Role for ${teammate.displayName}`)
    expect(within(adminRole).queryByRole('option', { name: 'Owner' })).not.toBeInTheDocument()
  })
})

