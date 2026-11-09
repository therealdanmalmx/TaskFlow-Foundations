import { apiRequest } from '../../../shared/api/HttpClient'
import type { BoardInput, BoardSummary } from '../model'

export function createBoard(workspaceId: string, input: BoardInput): Promise<BoardSummary> {
  return apiRequest<BoardSummary>(`/api/workspaces/${workspaceId}/boards`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}
