import { apiRequest } from '../../../shared/api/HttpClient'
import type { WorkspaceRole } from '../../workspaces/model'
import type { Member } from '../model'

export function addMember(workspaceId: string, username: string, role: WorkspaceRole): Promise<Member> {
  return apiRequest<Member>(`/api/workspaces/${workspaceId}/members`, {
    method: 'POST',
    body: JSON.stringify({ username, role }),
  })
}
