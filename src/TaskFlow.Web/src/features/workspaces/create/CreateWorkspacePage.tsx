import { useState, type FormEvent } from 'react'
import { ArrowRight, Building2, LayoutGrid, UsersRound } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Button } from '../../../shared/components/Button'
import { TextField } from '../../../shared/components/TextField'
import { useWorkspaces } from '../list/useWorkspaces'

export function CreateWorkspacePage() {
  const { workspaces, createWorkspace } = useWorkspaces()
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!name.trim() || isSubmitting) return
    setIsSubmitting(true)
    setError(null)
    try {
      const workspace = await createWorkspace(name.trim())
      navigate(`/workspaces/${workspace.id}/boards`, { replace: workspaces.length === 0 })
    } catch (requestError) {
      setError(apiErrorMessage(requestError, 'Could not create the workspace.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="mx-auto max-w-2xl py-4 sm:py-10">
      <div className="relative overflow-hidden rounded-3xl border border-line bg-surface p-6 shadow-sm sm:p-10">
        <div aria-hidden="true" className="absolute inset-x-0 top-0 h-1 bg-accent" />
        <span className="grid h-12 w-12 place-items-center rounded-2xl bg-accent/10 text-brand">
          <Building2 aria-hidden="true" className="h-6 w-6" />
        </span>
        <p className="mt-6 text-sm font-bold uppercase tracking-[0.14em] text-brand">A home for the work</p>
        <h1 className="mt-2 text-3xl font-semibold tracking-tight text-ink">
          {workspaces.length === 0 ? 'Create your first workspace' : 'Create another workspace'}
        </h1>
        <p className="mt-3 max-w-xl leading-7 text-muted">
          Workspaces keep boards, members, and permissions together. Use a team, company, or project name.
        </p>
        <div className="mt-6 grid grid-cols-2 gap-3 border-y border-line py-4 text-xs font-medium text-muted">
          <span className="flex items-center gap-2"><LayoutGrid className="h-4 w-4 text-brand" /> Organize your boards</span>
          <span className="flex items-center gap-2"><UsersRound className="h-4 w-4 text-brand" /> Bring your team together</span>
        </div>
        <form onSubmit={submit} aria-busy={isSubmitting} className="mt-8 grid gap-5">
          {error && <Alert>{error}</Alert>}
          <TextField
            label="Workspace name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="Acme Product Team"
            required
            maxLength={100}
            disabled={isSubmitting}
            autoFocus
          />
          <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
            {workspaces.length > 0 && <Button variant="secondary" onClick={() => navigate(-1)} disabled={isSubmitting}>Cancel</Button>}
            <Button type="submit" disabled={isSubmitting || !name.trim()}>
              {isSubmitting ? 'Creating…' : 'Create workspace'}
              <ArrowRight aria-hidden="true" className="h-4 w-4" />
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
