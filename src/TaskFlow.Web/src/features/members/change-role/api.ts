import { apiRequest } from '../../../shared/api/HttpClient'
import type { WorkspaceRole } from '../../workspaces/model'
import type { Member } from '../model'

export function changeMemberRole(workspaceId: string, userId: string, role: WorkspaceRole): Promise<Member> {
  return apiRequest<Member>(`/api/workspaces/${workspaceId}/members/${userId}/role`, {
    method: 'PUT',
    body: JSON.stringify({ role }),
  })
}
