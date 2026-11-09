import { apiRequest } from '../../../shared/api/HttpClient'
import type { AuthResponse, LoginInput } from '../model'

export function login(input: LoginInput): Promise<AuthResponse> {
  return apiRequest<AuthResponse>('/auth/login', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}
