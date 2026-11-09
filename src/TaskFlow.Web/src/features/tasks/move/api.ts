import { apiRequest } from '../../../shared/api/HttpClient'
import type { TaskItem, TaskState } from '../model'

export type TaskPlacement = 'top' | 'bottom' | 'before'

export function moveTask(taskId: string, status: TaskState, placement?: TaskPlacement, beforeTaskId?: string): Promise<TaskItem> {
  return apiRequest<TaskItem>(`/api/tasks/${taskId}/status`, {
    method: 'PUT',
    body: JSON.stringify({ status, placement, beforeTaskId }),
  })
}
