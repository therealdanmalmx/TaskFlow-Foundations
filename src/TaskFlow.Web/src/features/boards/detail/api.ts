import { apiRequest } from '../../../shared/api/HttpClient'
import type { BoardDetail, BoardInput } from '../model'

export function getBoard(boardId: string, signal?: AbortSignal): Promise<BoardDetail> {
  return apiRequest<BoardDetail>(`/api/boards/${boardId}`, { signal })
}

export function updateBoard(boardId: string, input: BoardInput): Promise<BoardDetail> {
  return apiRequest<BoardDetail>(`/api/boards/${boardId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export function setBoardArchived(boardId: string, isArchived: boolean): Promise<BoardDetail> {
  return apiRequest<BoardDetail>(`/api/boards/${boardId}/archive`, {
    method: 'PUT',
    body: JSON.stringify({ isArchived }),
  })
}
