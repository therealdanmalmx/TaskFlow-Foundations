import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from '../../App'
import {
  setupAppTests,
  json,
  stubApi,
  user,
  visit,
} from '../../test-support/appFixture'

setupAppTests()

describe('TaskFlow account settings', () => {
  it('updates profile details without losing the active session', async () => {
    visit('/settings/profile')
    stubApi({
      'PUT /api/users/me': (init) => json({ ...user, ...JSON.parse(String(init?.body)) }),
    })
    render(<App />)
    const actor = userEvent.setup()

    const displayName = await screen.findByRole('textbox', { name: 'Display name' })
    await actor.clear(displayName)
    await actor.type(displayName, 'Milan J.')
    await actor.click(screen.getByRole('button', { name: 'Save profile' }))

    expect(await screen.findByText('Your profile has been updated.')).toBeVisible()
    expect(screen.getByText('Milan J.')).toBeVisible()
  })

  it('shows a wrong-current-password validation error without ending the session', async () => {
    visit('/settings/security')
    stubApi({
      'PUT /api/users/me/password': () => json({ error: 'Current password is incorrect.' }, 400),
    })
    render(<App />)
    const actor = userEvent.setup()

    await actor.type(await screen.findByLabelText('Current password'), 'incorrect-password')
    await actor.type(screen.getByLabelText('New password'), 'a-different-secure-password')
    await actor.type(screen.getByLabelText('Confirm new password'), 'a-different-secure-password')
    await actor.click(screen.getByRole('button', { name: 'Change password' }))

    expect(await screen.findByText('Current password is incorrect.')).toBeVisible()
    expect(screen.getByRole('heading', { name: 'Settings' })).toBeVisible()
    expect(screen.getByText('@milan')).toBeVisible()
  })
})

