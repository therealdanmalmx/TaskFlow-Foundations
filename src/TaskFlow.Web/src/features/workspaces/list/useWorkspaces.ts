import { useContext } from 'react'
import { WorkspaceContext, type WorkspaceContextValue } from './context'

export function useWorkspaces(): WorkspaceContextValue {
  const context = useContext(WorkspaceContext)
  if (!context) throw new Error('useWorkspaces must be used inside WorkspaceProvider')
  return context
}
