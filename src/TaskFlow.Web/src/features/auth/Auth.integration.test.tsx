import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from '../../App'
import {
  setupAppTests,
  deferred,
  json,
  personalBoardSummary,
  personalWorkspace,
  routeKey,
  stubApi,
  user,
  visit,
} from '../../test-support/appFixture'

setupAppTests()

describe('TaskFlow authentication', () => {
  it('shows combined sign-in and registration with the course safety boundary', async () => {
    stubApi({ 'GET /api/users/me': () => json({ error: 'Not authenticated.' }, 401) })
    render(<App />)

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeVisible()
    expect(screen.getByRole('tab', { name: 'Register' })).toBeVisible()
    expect(screen.getByText(/Course demo fixture/)).toBeVisible()
    expect(screen.getByText(/JWT auth · v[12]/)).toBeVisible()

    await userEvent.click(screen.getByRole('tab', { name: 'Register' }))
    expect(screen.getByRole('button', { name: /Create account/ })).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'Display name' })).toBeVisible()
  })

  it('signs in through the cookie endpoint and opens the first workspace', async () => {
    const fetchMock = stubApi({
      'GET /api/users/me': () => json({ error: 'Not authenticated.' }, 401),
      'POST /auth/login': () => json({ token: 'ignored', expiresAt: '2099-01-01T00:00:00Z', username: user.username, user }),
    })
    render(<App />)
    const actor = userEvent.setup()

    await actor.type(await screen.findByRole('textbox', { name: 'Username' }), user.username)
    await actor.type(screen.getByLabelText('Password'), 'correct-horse-battery-staple')
    await actor.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Production launch')).toBeVisible()
    const loginCall = fetchMock.mock.calls.find(([input, init]) => routeKey(input, init) === 'POST /auth/login')
    expect(loginCall?.[1]).toMatchObject({ credentials: 'same-origin' })
    expect(JSON.parse(String(loginCall?.[1]?.body))).toEqual({ username: user.username, password: 'correct-horse-battery-staple' })
  })

  it('does not let a delayed session bootstrap overwrite a successful login', async () => {
    visit('/auth')
    const bootstrap = deferred<Response>()
    stubApi({
      'GET /api/users/me': () => bootstrap.promise,
      'POST /auth/login': () => json({ token: 'ignored', expiresAt: '2099-01-01T00:00:00Z', username: user.username, user }),
    })
    render(<App />)
    const actor = userEvent.setup()

    await actor.type(await screen.findByRole('textbox', { name: 'Username' }), user.username)
    await actor.type(screen.getByLabelText('Password'), 'correct-horse-battery-staple')
    await actor.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('Production launch')).toBeVisible()

    bootstrap.resolve(json({ error: 'Not authenticated.' }, 401))
    await waitFor(() => expect(screen.getByText('Production launch')).toBeVisible())
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument()
  })

  it('registers a user into the personal workspace and board created by the API', async () => {
    const fetchMock = stubApi({
      'GET /api/users/me': () => json({ error: 'Not authenticated.' }, 401),
      'POST /auth/register': () => json({ token: 'ignored', expiresAt: '2099-01-01T00:00:00Z', username: user.username, user }, 201),
      'GET /api/workspaces': () => json([personalWorkspace]),
      'GET /api/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards': () => json([personalBoardSummary]),
    })
    render(<App />)
    const actor = userEvent.setup()

    await actor.click(await screen.findByRole('tab', { name: 'Register' }))
    await actor.type(screen.getByRole('textbox', { name: 'Display name' }), user.displayName)
    await actor.type(screen.getByLabelText('Username'), user.username)
    await actor.type(screen.getByRole('textbox', { name: 'Email' }), user.email)
    await actor.type(screen.getByLabelText('Password'), 'correct-horse-battery-staple')
    await actor.click(screen.getByRole('button', { name: /Create account/ }))

    expect(await screen.findByText('My tasks')).toBeVisible()
    expect(screen.getByRole('option', { name: personalWorkspace.name })).toBeVisible()
    expect(window.location.pathname).toBe('/workspaces/7c5ac093-803a-4d65-9ff8-604883410010/boards')
    expect(fetchMock.mock.calls.some(([input, init]) => routeKey(input, init) === 'POST /api/workspaces')).toBe(false)
  })

  it('loads boards, identifies the signed-in user, and logs out server-side', async () => {
    stubApi()
    render(<App />)
    const actor = userEvent.setup()

    expect(await screen.findByText('Production launch')).toBeVisible()
    expect(screen.getByText('@milan')).toBeVisible()
    await actor.click(screen.getByRole('button', { name: /Milan Jovanović/ }))
    await actor.click(screen.getByRole('menuitem', { name: 'Log out everywhere' }))

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeVisible()
  })

  it('keeps the session visible when logout cannot be confirmed', async () => {
    stubApi({
      'POST /auth/logout': () => { throw new TypeError('Network unavailable') },
    })
    render(<App />)
    const actor = userEvent.setup()

    expect(await screen.findByText('Production launch')).toBeVisible()
    await actor.click(screen.getByRole('button', { name: /Milan Jovanović/ }))
    await actor.click(screen.getByRole('menuitem', { name: 'Log out everywhere' }))

    expect(await screen.findByText(/could not confirm sign out/i)).toBeVisible()
    expect(screen.getByText('Production launch')).toBeVisible()
  })
})

