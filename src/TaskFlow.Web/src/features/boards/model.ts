import type { WorkspaceRole } from '../workspaces/model'
import type { TaskItem } from '../tasks/model'

export interface BoardSummary {
  id: string
  workspaceId: string
  name: string
  description: string
  color: string
  isArchived: boolean
  taskCount: number
  updatedAt: string
}

export interface BoardDetail extends BoardSummary {
  role: WorkspaceRole
  tasks: TaskItem[]
}

export interface BoardInput {
  name: string
  description: string
  color: string
}

export const BOARD_COLORS = [
  { value: '#2563eb', label: 'Blue' },
  { value: '#7c3aed', label: 'Violet' },
  { value: '#db2777', label: 'Pink' },
  { value: '#ea580c', label: 'Orange' },
  { value: '#059669', label: 'Emerald' },
  { value: '#475569', label: 'Slate' },
] as const
