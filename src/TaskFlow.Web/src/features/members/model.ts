import type { WorkspaceRole } from '../workspaces/model'

export interface Member {
  userId: string
  username: string
  email: string
  displayName: string
  role: WorkspaceRole
  joinedAt: string
}
