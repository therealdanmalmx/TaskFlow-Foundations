export type TaskState = 'Todo' | 'Doing' | 'Done'
export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Urgent'

export interface TaskItem {
  id: string
  boardId: string
  title: string
  description: string
  status: TaskState
  priority: TaskPriority
  dueDate: string | null
  position: number
  createdByUserId: string
  createdByName: string
  assigneeId: string | null
  assigneeName: string | null
  createdAt: string
  updatedAt: string
  completedAt: string | null
}

export interface TaskInput {
  title: string
  description: string
  priority: TaskPriority
  dueDate: string | null
  assigneeId: string | null
}

export const TASK_STATES: TaskState[] = ['Todo', 'Doing', 'Done']
export const TASK_PRIORITIES: TaskPriority[] = ['Low', 'Medium', 'High', 'Urgent']
