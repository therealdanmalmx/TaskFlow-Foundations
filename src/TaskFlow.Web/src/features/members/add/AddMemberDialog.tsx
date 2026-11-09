import { useState, type FormEvent } from 'react'
import { UserPlus } from 'lucide-react'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Button } from '../../../shared/components/Button'
import { Dialog } from '../../../shared/components/Dialog'
import { SelectField } from '../../../shared/components/SelectField'
import { TextField } from '../../../shared/components/TextField'
import type { WorkspaceRole } from '../../workspaces/model'
import type { Member } from '../model'
import { addMember } from './api'

interface AddMemberDialogProps {
  open: boolean
  workspaceId: string
  canAssignOwner: boolean
  onClose: () => void
  onAdded: (member: Member) => void
}

export function AddMemberDialog({ open, workspaceId, canAssignOwner, onClose, onAdded }: AddMemberDialogProps) {
  const [username, setUsername] = useState('')
  const [role, setRole] = useState<WorkspaceRole>('Member')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!username.trim() || isSubmitting) return
    setIsSubmitting(true)
    setError(null)
    try {
      const member = await addMember(workspaceId, username.trim(), role)
      setUsername('')
      setRole('Member')
      onAdded(member)
    } catch (requestError) {
      setError(apiErrorMessage(requestError, 'Could not add the member.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Dialog open={open} onClose={onClose} title="Add a workspace member" description="Add an existing TaskFlow user by username." size="sm" closeDisabled={isSubmitting}>
      <form onSubmit={submit} aria-busy={isSubmitting} className="grid gap-5">
        {error && <Alert>{error}</Alert>}
        <TextField label="Username" value={username} onChange={(event) => setUsername(event.target.value)} placeholder="alex-morgan" required minLength={3} maxLength={50} pattern="[A-Za-z0-9._-]+" disabled={isSubmitting} />
        <SelectField label="Workspace role" value={role} onChange={(event) => setRole(event.target.value as WorkspaceRole)} disabled={isSubmitting}>
          {canAssignOwner && <option value="Owner">Owner — full control of the workspace</option>}
          <option value="Admin">Admin — manage boards and members</option>
          <option value="Member">Member — create and update work</option>
        </SelectField>
        <p className="rounded-xl bg-surface-muted p-3 text-xs leading-5 text-muted">Only owners can grant ownership. TaskFlow always preserves at least one owner for the workspace.</p>
        <div className="flex justify-end gap-3 border-t border-line pt-5">
          <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>Cancel</Button>
          <Button type="submit" disabled={isSubmitting || !username.trim()}><UserPlus className="h-4 w-4" /> {isSubmitting ? 'Adding…' : 'Add member'}</Button>
        </div>
      </form>
    </Dialog>
  )
}
