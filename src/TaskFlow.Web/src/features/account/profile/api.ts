import { apiRequest } from '../../../shared/api/HttpClient'
import type { User } from '../../auth/model'

export function updateProfile(displayName: string, email: string): Promise<User> {
  return apiRequest<User>('/api/users/me', {
    method: 'PUT',
    body: JSON.stringify({ displayName, email }),
  })
}
