import { apiRequest } from '../../../shared/api/HttpClient'
import type { BoardSummary } from '../model'

export function getBoards(workspaceId: string, signal?: AbortSignal): Promise<BoardSummary[]> {
  return apiRequest<BoardSummary[]>(`/api/workspaces/${workspaceId}/boards`, { signal })
}
