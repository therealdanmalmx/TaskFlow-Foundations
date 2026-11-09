export type WorkspaceRole = 'Owner' | 'Admin' | 'Member'

export interface Workspace {
  id: string
  name: string
  slug: string
  role: WorkspaceRole
  createdAt: string
}

export function canManageWorkspace(role: WorkspaceRole): boolean {
  return role === 'Owner' || role === 'Admin'
}

export function canEditWorkspace(role: WorkspaceRole): boolean {
  return role === 'Owner' || role === 'Admin' || role === 'Member'
}
