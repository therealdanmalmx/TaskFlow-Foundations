import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from '../../App'
import {
  setupAppTests,
  json,
  stubApi,
  workspace,
} from '../../test-support/appFixture'

setupAppTests()

describe('TaskFlow workspaces', () => {
  it('lets an authenticated account with no memberships create a workspace', async () => {
    stubApi({
      'GET /api/workspaces': () => json([]),
      'POST /api/workspaces': () => json(workspace, 201),
    })
    render(<App />)
    const actor = userEvent.setup()

    expect(await screen.findByRole('heading', { name: 'Create your first workspace' })).toBeVisible()
    await actor.type(screen.getByRole('textbox', { name: 'Workspace name' }), workspace.name)
    await actor.click(screen.getByRole('button', { name: 'Create workspace' }))
    expect(await screen.findByText('Production launch')).toBeVisible()
  })

  it('does not interpret a failed workspace request as an empty account', async () => {
    stubApi({
      'GET /api/workspaces': () => json({ error: 'Workspaces are temporarily unavailable.' }, 503),
    })
    render(<App />)

    expect(await screen.findByText('Workspaces are temporarily unavailable.')).toBeVisible()
    expect(screen.queryByRole('heading', { name: 'Create your first workspace' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible()
  })
})

