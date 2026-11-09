import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { createWorkspace as createWorkspaceRequest } from '../create/api'
import type { Workspace } from '../model'
import { getWorkspaces } from './api'
import { WorkspaceContext } from './context'

export function WorkspaceProvider({ children }: { children: ReactNode }) {
  const [workspaces, setWorkspaces] = useState<Workspace[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const mountedRef = useRef(true)
  const refreshGenerationRef = useRef(0)
  const refreshControllerRef = useRef<AbortController | null>(null)

  const refresh = useCallback(async () => {
    const generation = ++refreshGenerationRef.current
    refreshControllerRef.current?.abort()
    const controller = new AbortController()
    refreshControllerRef.current = controller
    setIsLoading(true)
    setError(null)
    try {
      const loadedWorkspaces = await getWorkspaces(controller.signal)
      if (!mountedRef.current || controller.signal.aborted || generation !== refreshGenerationRef.current) return
      setWorkspaces(loadedWorkspaces)
    } catch (requestError) {
      if (!mountedRef.current || controller.signal.aborted || generation !== refreshGenerationRef.current) return
      setError(apiErrorMessage(requestError, 'Could not load your workspaces.'))
    } finally {
      if (mountedRef.current && generation === refreshGenerationRef.current) setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    mountedRef.current = true
    void refresh()
    return () => {
      mountedRef.current = false
      refreshGenerationRef.current += 1
      refreshControllerRef.current?.abort()
    }
  }, [refresh])

  const createWorkspace = useCallback(async (name: string) => {
    const created = await createWorkspaceRequest(name)
    if (!mountedRef.current) return created
    refreshGenerationRef.current += 1
    refreshControllerRef.current?.abort()
    refreshControllerRef.current = null
    setWorkspaces((current) => current.some((workspace) => workspace.id === created.id)
      ? current.map((workspace) => workspace.id === created.id ? created : workspace)
      : [...current, created])
    setIsLoading(false)
    setError(null)
    return created
  }, [])

  const value = useMemo(() => ({ workspaces, isLoading, error, refresh, createWorkspace }), [createWorkspace, error, isLoading, refresh, workspaces])
  return <WorkspaceContext.Provider value={value}>{children}</WorkspaceContext.Provider>
}
