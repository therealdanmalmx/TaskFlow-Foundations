import { apiRequest } from '../../../shared/api/HttpClient'
import type { AuthResponse, RegisterInput } from '../model'

export function register(input: RegisterInput): Promise<AuthResponse> {
  return apiRequest<AuthResponse>('/auth/register', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}
