import { AlertTriangle } from 'lucide-react'
import { Alert } from './Alert'
import { Button } from './Button'
import { Dialog } from './Dialog'

interface ConfirmDialogProps {
  open: boolean
  title: string
  description: string
  confirmLabel: string
  error?: string | null
  isPending?: boolean
  onClose: () => void
  onConfirm: () => void
}

export function ConfirmDialog({ open, title, description, confirmLabel, error, isPending, onClose, onConfirm }: ConfirmDialogProps) {
  return (
    <Dialog open={open} onClose={onClose} title={title} size="sm" closeDisabled={isPending}>
      <div className="flex gap-3 rounded-xl bg-red-50 p-4 text-red-900 dark:bg-red-400/10 dark:text-red-200">
        <AlertTriangle aria-hidden="true" className="mt-0.5 h-5 w-5 shrink-0" />
        <p className="text-sm leading-6">{description}</p>
      </div>
      {error && <Alert className="mt-4">{error}</Alert>}
      <div className="mt-6 flex justify-end gap-3">
        <Button variant="secondary" onClick={onClose} disabled={isPending}>Cancel</Button>
        <Button variant="danger" onClick={onConfirm} disabled={isPending}>
          {isPending ? 'Working…' : confirmLabel}
        </Button>
      </div>
    </Dialog>
  )
}
