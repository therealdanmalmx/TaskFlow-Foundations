import { useEffect, useState, type FormEvent } from 'react'
import { Save } from 'lucide-react'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Button } from '../../../shared/components/Button'
import { Dialog } from '../../../shared/components/Dialog'
import { SelectField } from '../../../shared/components/SelectField'
import { TextAreaField } from '../../../shared/components/TextAreaField'
import { TextField } from '../../../shared/components/TextField'
import { RequestStatusNotice, type RequestStatus } from '../../../shared/components/RequestStatusNotice'
import type { Member } from '../../members/model'
import { TASK_PRIORITIES, type TaskInput, type TaskItem, type TaskPriority } from '../model'

interface TaskEditorDialogProps {
  open: boolean
  task: TaskItem | null
  members: Member[]
  membersStatus: RequestStatus
  onRetryMembers: () => void
  onClose: () => void
  onSave: (input: TaskInput) => Promise<void>
}

export function TaskEditorDialog({ open, task, members, membersStatus, onRetryMembers, onClose, onSave }: TaskEditorDialogProps) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [priority, setPriority] = useState<TaskPriority>('Medium')
  const [dueDate, setDueDate] = useState('')
  const [assigneeId, setAssigneeId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    if (!open) return
    setTitle(task?.title ?? '')
    setDescription(task?.description ?? '')
    setPriority(task?.priority ?? 'Medium')
    setDueDate(task?.dueDate?.slice(0, 10) ?? '')
    setAssigneeId(task?.assigneeId ?? '')
    setError(null)
  }, [open, task])

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!title.trim() || isSubmitting) return
    setIsSubmitting(true)
    setError(null)
    try {
      await onSave({
        title: title.trim(),
        description: description.trim(),
        priority,
        dueDate: dueDate || null,
        assigneeId: assigneeId || null,
      })
      onClose()
    } catch (requestError) {
      setError(apiErrorMessage(requestError, `Could not ${task ? 'update' : 'create'} the task.`))
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Dialog open={open} onClose={onClose} title={task ? 'Edit task' : 'Create a task'} description={task ? 'Keep the details and ownership current.' : 'Capture enough context for someone else to move it forward.'} size="lg" closeDisabled={isSubmitting}>
      <form onSubmit={submit} aria-busy={isSubmitting} className="grid gap-5">
        {error && <Alert>{error}</Alert>}
        <TextField label="Title" value={title} onChange={(event) => setTitle(event.target.value)} placeholder="What needs to happen?" maxLength={200} required disabled={isSubmitting} />
        <TextAreaField label="Description" value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Add context, decisions, or a useful definition of done." maxLength={4000} disabled={isSubmitting} />
        <RequestStatusNotice status={membersStatus} label="Member list" hasPreviousData={members.length > 0} onRetry={onRetryMembers} />
        {membersStatus !== 'ready' && <p className="text-sm text-muted">Assignments stay unchanged until the member list is available.</p>}
        <div className="grid gap-4 sm:grid-cols-3">
          <SelectField label="Priority" value={priority} onChange={(event) => setPriority(event.target.value as TaskPriority)} disabled={isSubmitting}>
            {TASK_PRIORITIES.map((value) => <option key={value} value={value}>{value}</option>)}
          </SelectField>
          <TextField label="Due date" type="date" value={dueDate} onChange={(event) => setDueDate(event.target.value)} disabled={isSubmitting} />
          <SelectField label="Assignee" value={assigneeId} onChange={(event) => setAssigneeId(event.target.value)} disabled={isSubmitting || membersStatus !== 'ready'}>
            <option value="">Unassigned</option>
            {task?.assigneeId && !members.some((member) => member.userId === task.assigneeId) && <option value={task.assigneeId}>{task.assigneeName ?? 'Current assignee'}</option>}
            {members.map((member) => <option key={member.userId} value={member.userId}>{member.displayName}</option>)}
          </SelectField>
        </div>
        <div className="mt-2 flex justify-end gap-3 border-t border-line pt-5">
          <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>Cancel</Button>
          <Button type="submit" disabled={isSubmitting || !title.trim()}><Save aria-hidden="true" className="h-4 w-4" /> {isSubmitting ? 'Saving…' : task ? 'Save task' : 'Create task'}</Button>
        </div>
      </form>
    </Dialog>
  )
}
