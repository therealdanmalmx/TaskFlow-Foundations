import { useState, type FormEvent } from 'react'
import { KeyRound, ShieldCheck } from 'lucide-react'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Button } from '../../../shared/components/Button'
import { TextField } from '../../../shared/components/TextField'
import { SettingsNavigation } from '../SettingsNavigation'
import { changePassword } from './api'

export function PasswordPage() {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const confirmationError = confirmation && confirmation !== newPassword ? 'Passwords do not match.' : undefined

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (newPassword.length < 12 || newPassword !== confirmation || isSubmitting) return
    setIsSubmitting(true)
    setError(null)
    setSuccess(false)
    try {
      await changePassword(currentPassword, newPassword)
      setCurrentPassword('')
      setNewPassword('')
      setConfirmation('')
      setSuccess(true)
    } catch (requestError) {
      setError(apiErrorMessage(requestError, 'Could not change your password.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="mx-auto max-w-5xl">
      <header>
        <p className="text-sm font-bold uppercase tracking-[0.14em] text-brand">Your account</p>
        <h1 className="mt-1 text-3xl font-bold tracking-tight text-ink">Settings</h1>
        <p className="mt-2 text-sm text-muted">Keep your identity and sign-in details current.</p>
      </header>
      <div className="mt-8 grid gap-8 lg:grid-cols-[14rem_minmax(0,1fr)]">
        <SettingsNavigation />
        <section aria-labelledby="security-heading" className="rounded-2xl border border-line bg-surface p-5 shadow-sm sm:p-7">
          <div className="flex items-center gap-4 border-b border-line pb-6">
            <span className="grid h-12 w-12 place-items-center rounded-2xl bg-accent/10 text-brand"><ShieldCheck className="h-6 w-6" /></span>
            <div>
              <h2 id="security-heading" className="text-lg font-bold text-ink">Password & security</h2>
              <p className="mt-1 text-sm text-muted">Use a unique password that is hard to guess.</p>
            </div>
          </div>
          <form onSubmit={submit} aria-busy={isSubmitting} className="mt-6 grid max-w-xl gap-5">
            {error && <Alert>{error}</Alert>}
            {success && <Alert tone="success">Your password has been changed.</Alert>}
            <TextField label="Current password" type="password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} autoComplete="current-password" required maxLength={200} disabled={isSubmitting} />
            <TextField label="New password" type="password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} autoComplete="new-password" required minLength={12} maxLength={200} hint="Use 12 to 200 characters and avoid reused passwords." disabled={isSubmitting} />
            <TextField label="Confirm new password" type="password" value={confirmation} onChange={(event) => setConfirmation(event.target.value)} autoComplete="new-password" required maxLength={200} error={confirmationError} disabled={isSubmitting} />
            <div className="flex justify-end border-t border-line pt-5">
              <Button type="submit" disabled={isSubmitting || !currentPassword || newPassword.length < 12 || newPassword !== confirmation}><KeyRound className="h-4 w-4" /> {isSubmitting ? 'Changing…' : 'Change password'}</Button>
            </div>
          </form>
        </section>
      </div>
    </div>
  )
}
