import { useState, type FormEvent } from 'react'
import { Plus } from 'lucide-react'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Button } from '../../../shared/components/Button'
import { Dialog } from '../../../shared/components/Dialog'
import { TextAreaField } from '../../../shared/components/TextAreaField'
import { TextField } from '../../../shared/components/TextField'
import { cn } from '../../../shared/lib/cn'
import { BOARD_COLORS, type BoardSummary } from '../model'
import { createBoard } from './api'

interface CreateBoardDialogProps {
  open: boolean
  workspaceId: string
  onClose: () => void
  onCreated: (board: BoardSummary) => void
}

export function CreateBoardDialog({ open, workspaceId, onClose, onCreated }: CreateBoardDialogProps) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [color, setColor] = useState<string>(BOARD_COLORS[0].value)
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  function close() {
    if (!isSubmitting) onClose()
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!name.trim() || isSubmitting) return
    setIsSubmitting(true)
    setError(null)
    try {
      const board = await createBoard(workspaceId, { name: name.trim(), description: description.trim(), color })
      setName('')
      setDescription('')
      setColor(BOARD_COLORS[0].value)
      onCreated(board)
    } catch (requestError) {
      setError(apiErrorMessage(requestError, 'Could not create the board.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Dialog open={open} onClose={close} title="Create a board" description="Give the work a clear home and a recognizable color." closeDisabled={isSubmitting}>
      <form onSubmit={submit} aria-busy={isSubmitting} className="grid gap-5">
        {error && <Alert>{error}</Alert>}
        <TextField label="Board name" value={name} onChange={(event) => setName(event.target.value)} placeholder="Product launch" maxLength={100} required disabled={isSubmitting} />
        <TextAreaField label="Description" value={description} onChange={(event) => setDescription(event.target.value)} placeholder="What is this board responsible for?" maxLength={2000} disabled={isSubmitting} />
        <fieldset>
          <legend className="mb-2 text-sm font-medium text-ink">Board color</legend>
          <div className="flex flex-wrap gap-2.5">
            {BOARD_COLORS.map((option) => (
              <label key={option.value} className="cursor-pointer">
                <input className="peer sr-only" type="radio" name="board-color" value={option.value} checked={color === option.value} onChange={() => setColor(option.value)} disabled={isSubmitting} />
                <span
                  title={option.label}
                  className={cn('block h-9 w-9 rounded-full border-4 border-surface shadow-sm ring-1 ring-line transition peer-checked:scale-110 peer-checked:ring-2 peer-checked:ring-accent peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-accent')}
                  style={{ backgroundColor: option.value }}
                />
                <span className="sr-only">{option.label}</span>
              </label>
            ))}
          </div>
        </fieldset>
        <div className="mt-2 flex justify-end gap-3 border-t border-line pt-5">
          <Button variant="secondary" onClick={close} disabled={isSubmitting}>Cancel</Button>
          <Button type="submit" disabled={isSubmitting || !name.trim()}>
            <Plus aria-hidden="true" className="h-4 w-4" />
            {isSubmitting ? 'Creating…' : 'Create board'}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}
