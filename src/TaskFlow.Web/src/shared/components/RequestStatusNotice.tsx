import { Alert } from './Alert'
import { Button } from './Button'

export type RequestStatus = 'loading' | 'ready' | 'error'

export function RequestStatusNotice({ status, label, hasPreviousData = false, onRetry }: {
  status: RequestStatus
  label: string
  hasPreviousData?: boolean
  onRetry: () => void
}) {
  if (status === 'ready' || (status === 'loading' && !hasPreviousData)) return null

  return (
    <Alert tone={status === 'error' ? 'error' : 'info'}>
      <p>{status === 'error' ? `${label} could not be loaded.` : `Refreshing ${label.toLowerCase()}…`} {hasPreviousData && 'Showing the last successful result.'}</p>
      {status === 'error' && <Button variant="secondary" size="sm" className="mt-2" onClick={onRetry}>Retry {label.toLowerCase()}</Button>}
    </Alert>
  )
}
