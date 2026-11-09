export const unauthorizedEvent = 'taskflow:unauthorized'

type ErrorPayload = {
  error?: string
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: Record<string, string[]>

  constructor(message: string, status: number, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

const DEFAULT_TIMEOUT_MS = 12_000

function createRequestSignal(callerSignal?: AbortSignal | null): AbortSignal {
  const timeoutSignal = AbortSignal.timeout(DEFAULT_TIMEOUT_MS)
  return callerSignal ? AbortSignal.any([callerSignal, timeoutSignal]) : timeoutSignal
}

async function parseBody(response: Response): Promise<unknown> {
  if (response.status === 204) return undefined
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('json')) return response.text()
  return response.json()
}

function throwIfAborted(signal: AbortSignal | null | undefined): void {
  if (signal?.aborted) throw new DOMException('The request was aborted.', 'AbortError')
}

function errorMessage(payload: unknown, response: Response): { message: string; fields: Record<string, string[]> } {
  if (payload && typeof payload === 'object') {
    const problem = payload as ErrorPayload
    return {
      message: problem.error ?? problem.detail ?? problem.title ?? response.statusText,
      fields: problem.errors ?? {},
    }
  }

  return {
    message: typeof payload === 'string' && payload.trim() ? payload : response.statusText,
    fields: {},
  }
}

export async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  const signal = createRequestSignal(init.signal)
  if (init.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(path, {
    ...init,
    credentials: 'same-origin',
    headers,
    signal,
  })
  throwIfAborted(signal)
  const payload = await parseBody(response)
  throwIfAborted(signal)

  if (!response.ok) {
    const { message, fields } = errorMessage(payload, response)
    if (response.status === 401) window.dispatchEvent(new Event(unauthorizedEvent))
    throw new ApiError(message, response.status, fields)
  }

  return payload as T
}

export async function apiRequestWithResponse<T>(path: string, init: RequestInit = {}): Promise<{ data: T; response: Response }> {
  const headers = new Headers(init.headers)
  const signal = createRequestSignal(init.signal)
  if (init.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(path, {
    ...init,
    credentials: 'same-origin',
    headers,
    signal,
  })
  throwIfAborted(signal)
  const payload = await parseBody(response)
  throwIfAborted(signal)

  if (!response.ok) {
    const { message, fields } = errorMessage(payload, response)
    if (response.status === 401) window.dispatchEvent(new Event(unauthorizedEvent))
    throw new ApiError(message, response.status, fields)
  }

  return { data: payload as T, response }
}

export function apiErrorMessage(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError)) return fallback
  const genericResponseMessages = new Set(['Unauthorized', 'Forbidden', 'Not Found', 'Conflict'])
  if (error.message && !genericResponseMessages.has(error.message)) return error.message
  if (error.status === 401) return 'Your session has expired. Sign in again.'
  if (error.status === 403) return 'You do not have permission to do that.'
  if (error.status === 404) return 'That item could not be found or you no longer have access.'
  if (error.status === 409) return 'That change conflicts with the current state. Refresh and try again.'
  return fallback
}
