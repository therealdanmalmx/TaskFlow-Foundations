import { apiRequest } from '../../../shared/api/HttpClient'
import type { TaskInput, TaskItem } from '../model'

export function createTask(boardId: string, input: TaskInput): Promise<TaskItem> {
  return apiRequest<TaskItem>(`/api/boards/${boardId}/tasks`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}
