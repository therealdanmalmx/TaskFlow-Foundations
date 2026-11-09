import { apiRequest } from '../../../shared/api/HttpClient'
import type { Member } from '../model'

export function getMembers(workspaceId: string, signal?: AbortSignal): Promise<Member[]> {
  return apiRequest<Member[]>(`/api/workspaces/${workspaceId}/members`, { signal })
}
