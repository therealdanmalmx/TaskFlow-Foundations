import { apiRequest } from '../../../shared/api/HttpClient'

export function removeMember(workspaceId: string, userId: string): Promise<void> {
  return apiRequest<void>(`/api/workspaces/${workspaceId}/members/${userId}`, { method: 'DELETE' })
}
