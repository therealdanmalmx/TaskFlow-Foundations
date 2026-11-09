import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Workspace } from '../model'
import { WorkspaceProvider } from './WorkspaceContext'
import { useWorkspaces } from './useWorkspaces'

const firstWorkspace: Workspace = {
  id: '7c5ac093-803a-4d65-9ff8-604883410001',
  name: 'Earlier workspace',
  slug: 'earlier',
  role: 'Owner',
  createdAt: '2026-01-01T00:00:00Z',
}

const latestWorkspace: Workspace = {
  ...firstWorkspace,
  id: '7c5ac093-803a-4d65-9ff8-604883410002',
  name: 'Latest workspace',
  slug: 'latest',
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((complete) => { resolve = complete })
  return { promise, resolve }
}

function WorkspaceHarness() {
  const { workspaces, refresh, createWorkspace } = useWorkspaces()
  return (
    <div>
      <ul>{workspaces.map((workspace) => <li key={workspace.id}>{workspace.name}</li>)}</ul>
      <button type="button" onClick={() => void refresh()}>Refresh</button>
      <button type="button" onClick={() => void createWorkspace(latestWorkspace.name)}>Create</button>
    </div>
  )
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('WorkspaceProvider concurrency', () => {
  it('keeps the newest refresh when responses complete out of order', async () => {
    const earlier = deferred<Response>()
    const latest = deferred<Response>()
    let requestCount = 0
    const fetchMock = vi.fn(() => requestCount++ === 0 ? earlier.promise : latest.promise)
    vi.stubGlobal('fetch', fetchMock)
    render(<WorkspaceProvider><WorkspaceHarness /></WorkspaceProvider>)

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))

    latest.resolve(json([latestWorkspace]))
    expect(await screen.findByText(latestWorkspace.name)).toBeVisible()
    earlier.resolve(json([firstWorkspace]))
    await earlier.promise

    await waitFor(() => expect(screen.getByText(latestWorkspace.name)).toBeVisible())
    expect(screen.queryByText(firstWorkspace.name)).not.toBeInTheDocument()
  })

  it('does not let an older refresh erase a workspace that was just created', async () => {
    const staleRefresh = deferred<Response>()
    let getCount = 0
    const fetchMock = vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
      if ((init?.method ?? 'GET') === 'POST') return Promise.resolve(json(latestWorkspace, 201))
      if (getCount++ === 0) return Promise.resolve(json([firstWorkspace]))
      return staleRefresh.promise
    })
    vi.stubGlobal('fetch', fetchMock)
    render(<WorkspaceProvider><WorkspaceHarness /></WorkspaceProvider>)

    expect(await screen.findByText(firstWorkspace.name)).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    await waitFor(() => expect(getCount).toBe(2))
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    expect(await screen.findByText(latestWorkspace.name)).toBeVisible()

    staleRefresh.resolve(json([firstWorkspace]))
    await staleRefresh.promise
    await waitFor(() => expect(screen.getByText(latestWorkspace.name)).toBeVisible())
    expect(screen.getByText(firstWorkspace.name)).toBeVisible()
  })
})
