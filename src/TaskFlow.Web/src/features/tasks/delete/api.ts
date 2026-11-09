import { apiRequest } from '../../../shared/api/HttpClient'

export function deleteTask(taskId: string): Promise<void> {
  return apiRequest<void>(`/api/tasks/${taskId}`, { method: 'DELETE' })
}
