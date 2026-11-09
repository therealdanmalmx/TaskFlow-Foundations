import { createContext } from 'react'
import type { Workspace } from '../model'

export interface WorkspaceContextValue {
  workspaces: Workspace[]
  isLoading: boolean
  error: string | null
  refresh: () => Promise<void>
  createWorkspace: (name: string) => Promise<Workspace>
}

export const WorkspaceContext = createContext<WorkspaceContextValue | null>(null)
