import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiErrorMessage, apiRequest } from './HttpClient'

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

function waitForAbort(_input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const signal = init?.signal
  if (!signal) return Promise.reject(new Error('Expected a request signal.'))
  return new Promise((_resolve, reject) => {
    if (signal.aborted) reject(signal.reason)
    else signal.addEventListener('abort', () => reject(signal.reason), { once: true })
  })
}

describe('HttpClient', () => {
  it('sends same-origin credentials and JSON by default', async () => {
    const fetchMock = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => Promise.resolve(new Response(JSON.stringify({ ok: true }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })))
    vi.stubGlobal('fetch', fetchMock)

    await apiRequest('/api/example', { method: 'POST', body: JSON.stringify({ name: 'TaskFlow' }) })

    expect(fetchMock).toHaveBeenCalledWith('/api/example', expect.objectContaining({
      method: 'POST',
      credentials: 'same-origin',
    }))
    const headers = fetchMock.mock.calls[0][1]?.headers as Headers
    expect(headers.get('Content-Type')).toBe('application/json')
  })

  it('preserves a structured API error and status', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response(JSON.stringify({ error: 'That username is already in use.' }), {
      status: 409,
      statusText: 'Conflict',
      headers: { 'Content-Type': 'application/json' },
    }))))

    const error = await apiRequest('/api/example').catch((reason: unknown) => reason)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 409, message: 'That username is already in use.' })
    expect(apiErrorMessage(error, 'Fallback')).toBe('That username is already in use.')
  })

  it('provides useful messages for bare authorization responses', () => {
    expect(apiErrorMessage(new ApiError('Forbidden', 403), 'Fallback')).toBe('You do not have permission to do that.')
    expect(apiErrorMessage(new ApiError('Not Found', 404), 'Fallback')).toContain('no longer have access')
    expect(apiErrorMessage(new ApiError('Conflict', 409), 'Fallback')).toContain('current state')
  })

  it('honors a caller abort while retaining the request timeout', async () => {
    const timeout = new AbortController()
    const timeoutSpy = vi.spyOn(AbortSignal, 'timeout').mockReturnValue(timeout.signal)
    const fetchMock = vi.fn(waitForAbort)
    vi.stubGlobal('fetch', fetchMock)
    const caller = new AbortController()

    const request = apiRequest('/api/slow', { signal: caller.signal })
    caller.abort()

    await expect(request).rejects.toMatchObject({ name: 'AbortError' })
    expect(timeoutSpy).toHaveBeenCalledWith(12_000)
    expect(fetchMock.mock.calls[0][1]?.signal).not.toBe(caller.signal)
  })

  it('times out a request that also has a caller signal without waiting 12 seconds', async () => {
    const timeout = new AbortController()
    vi.spyOn(AbortSignal, 'timeout').mockReturnValue(timeout.signal)
    vi.stubGlobal('fetch', vi.fn(waitForAbort))
    const caller = new AbortController()

    const request = apiRequest('/api/slow', { signal: caller.signal })
    timeout.abort(new DOMException('The operation timed out.', 'TimeoutError'))

    await expect(request).rejects.toMatchObject({ name: 'TimeoutError' })
    expect(caller.signal.aborted).toBe(false)
  })
})
