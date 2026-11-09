import { apiRequest } from '../../../shared/api/HttpClient'
import type { TaskInput, TaskItem } from '../model'

export function updateTask(taskId: string, input: TaskInput): Promise<TaskItem> {
  return apiRequest<TaskItem>(`/api/tasks/${taskId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}
