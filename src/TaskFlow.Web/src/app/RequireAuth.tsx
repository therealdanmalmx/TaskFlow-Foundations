import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../features/auth/session/useAuth'
import { Alert } from '../shared/components/Alert'
import { Button } from '../shared/components/Button'
import { LoadingState } from '../shared/components/LoadingState'

export function RequireAuth() {
  const { user, isLoading, sessionError, retrySession } = useAuth()
  if (isLoading) return <div className="min-h-screen bg-canvas"><LoadingState label="Opening your workspace…" /></div>
  if (sessionError) {
    return (
      <main className="grid min-h-screen place-items-center bg-canvas p-6">
        <div className="w-full max-w-md">
          <Alert>{sessionError}</Alert>
          <Button className="mt-4 w-full" onClick={() => void retrySession()}>Try again</Button>
        </div>
      </main>
    )
  }
  if (!user) return <Navigate to="/auth" replace />
  return <Outlet />
}
