import { Navigate } from 'react-router-dom'
import { useWorkspaces } from '../features/workspaces/list/useWorkspaces'
import { Alert } from '../shared/components/Alert'
import { Button } from '../shared/components/Button'
import { LoadingState } from '../shared/components/LoadingState'

export function HomeRedirect() {
  const { workspaces, isLoading, error, refresh } = useWorkspaces()
  if (isLoading) return <LoadingState label="Loading workspaces…" />
  if (error) {
    return (
      <div className="mx-auto max-w-2xl py-12">
        <Alert>{error}</Alert>
        <Button className="mt-4" onClick={() => void refresh()}>Try again</Button>
      </div>
    )
  }
  if (workspaces.length === 0) return <Navigate to="/workspaces/new" replace />
  return <Navigate to={`/workspaces/${workspaces[0].id}/boards`} replace />
}
