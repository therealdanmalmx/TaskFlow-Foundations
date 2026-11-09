import { apiRequest } from '../../../shared/api/HttpClient'
import type { User } from '../model'

export function getCurrentUser(signal?: AbortSignal): Promise<User> {
  return apiRequest<User>('/api/users/me', { signal })
}

export function logout(): Promise<void> {
  return apiRequest<void>('/auth/logout', { method: 'POST' })
}
