import { apiRequest } from '../../../shared/api/HttpClient'
import type { Workspace } from '../model'

export function getWorkspaces(signal?: AbortSignal): Promise<Workspace[]> {
  return apiRequest<Workspace[]>('/api/workspaces', { signal })
}
