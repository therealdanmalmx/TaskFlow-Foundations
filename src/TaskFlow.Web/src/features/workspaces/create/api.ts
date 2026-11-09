import { apiRequest } from '../../../shared/api/HttpClient'
import type { Workspace } from '../model'

export function createWorkspace(name: string): Promise<Workspace> {
  return apiRequest<Workspace>('/api/workspaces', {
    method: 'POST',
    body: JSON.stringify({ name }),
  })
}
