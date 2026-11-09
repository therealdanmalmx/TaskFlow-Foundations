import { useEffect, useState, type FormEvent } from 'react'
import { Archive, ArchiveRestore, Save } from 'lucide-react'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Button } from '../../../shared/components/Button'
import { Dialog } from '../../../shared/components/Dialog'
import { TextAreaField } from '../../../shared/components/TextAreaField'
import { TextField } from '../../../shared/components/TextField'
import { cn } from '../../../shared/lib/cn'
import { BOARD_COLORS, type BoardDetail, type BoardInput } from '../model'

interface BoardSettingsDialogProps {
  board: BoardDetail
  open: boolean
  onClose: () => void
  onSave: (input: BoardInput) => Promise<void>
  onArchive: (archived: boolean) => Promise<void>
}

export function BoardSettingsDialog({ board, open, onClose, onSave, onArchive }: BoardSettingsDialogProps) {
  const [name, setName] = useState(board.name)
  const [description, setDescription] = useState(board.description)
  const [color, setColor] = useState(board.color)
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    if (!open) return
    setName(board.name)
    setDescription(board.description)
    setColor(board.color)
    setError(null)
  }, [board, open])

  async function perform(action: () => Promise<void>) {
    setIsSubmitting(true)
    setError(null)
    try {
      await action()
    } catch (requestError) {
      setError(apiErrorMessage(requestError, 'Could not update the board.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    if (!name.trim() || isSubmitting) return
    void perform(async () => {
      await onSave({ name: name.trim(), description: description.trim(), color })
      onClose()
    })
  }

  return (
    <Dialog open={open} onClose={onClose} title="Board settings" description="Update how this board appears across the workspace." closeDisabled={isSubmitting}>
      <form onSubmit={submit} aria-busy={isSubmitting} className="grid gap-5">
        {error && <Alert>{error}</Alert>}
        <TextField label="Board name" value={name} onChange={(event) => setName(event.target.value)} maxLength={100} required disabled={isSubmitting} />
        <TextAreaField label="Description" value={description} onChange={(event) => setDescription(event.target.value)} maxLength={2000} disabled={isSubmitting} />
        <fieldset>
          <legend className="mb-2 text-sm font-medium text-ink">Board color</legend>
          <div className="flex flex-wrap gap-2.5">
            {BOARD_COLORS.map((option) => (
              <label key={option.value} className="cursor-pointer">
                <input className="peer sr-only" type="radio" name="settings-board-color" value={option.value} checked={color === option.value} onChange={() => setColor(option.value)} disabled={isSubmitting} />
                <span className={cn('block h-9 w-9 rounded-full border-4 border-surface shadow-sm ring-1 ring-line transition peer-checked:scale-110 peer-checked:ring-2 peer-checked:ring-accent peer-focus-visible:outline-2 peer-focus-visible:outline-offset-4 peer-focus-visible:outline-accent')} style={{ backgroundColor: option.value }} title={option.label} />
                <span className="sr-only">{option.label}</span>
              </label>
            ))}
          </div>
        </fieldset>
        <div className="mt-2 flex flex-col-reverse gap-3 border-t border-line pt-5 sm:flex-row sm:items-center">
          <Button
            variant="ghost"
            className={board.isArchived ? 'text-emerald-700 hover:bg-emerald-50 dark:text-emerald-300 dark:hover:bg-emerald-400/10' : 'text-red-700 hover:bg-red-50 dark:text-red-300 dark:hover:bg-red-400/10'}
            disabled={isSubmitting}
            onClick={() => void perform(() => onArchive(!board.isArchived))}
          >
            {board.isArchived ? <ArchiveRestore className="h-4 w-4" /> : <Archive className="h-4 w-4" />}
            {board.isArchived ? 'Restore board' : 'Archive board'}
          </Button>
          <div className="flex flex-1 justify-end gap-3">
            <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>Cancel</Button>
            <Button type="submit" disabled={isSubmitting || !name.trim()}><Save className="h-4 w-4" /> {isSubmitting ? 'Saving…' : 'Save changes'}</Button>
          </div>
        </div>
      </form>
    </Dialog>
  )
}
