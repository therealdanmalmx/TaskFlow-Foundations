import { useState, type FormEvent } from 'react'
import { Save, UserRound } from 'lucide-react'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Avatar } from '../../../shared/components/Avatar'
import { Button } from '../../../shared/components/Button'
import { TextField } from '../../../shared/components/TextField'
import { useAuth } from '../../auth/session/useAuth'
import { SettingsNavigation } from '../SettingsNavigation'
import { updateProfile } from './api'

export function ProfilePage() {
  const { user, updateUser } = useAuth()
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [email, setEmail] = useState(user?.email ?? '')
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)

  if (!user) return null

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!displayName.trim() || !email.trim() || isSubmitting) return
    setIsSubmitting(true)
    setError(null)
    setSuccess(false)
    try {
      const updated = await updateProfile(displayName.trim(), email.trim())
      updateUser(updated)
      setSuccess(true)
    } catch (requestError) {
      setError(apiErrorMessage(requestError, 'Could not update your profile.'))
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
        <section aria-labelledby="profile-heading" className="rounded-2xl border border-line bg-surface p-5 shadow-sm sm:p-7">
          <div className="flex items-center gap-4 border-b border-line pb-6">
            <Avatar name={displayName || user.username} size="lg" />
            <div>
              <h2 id="profile-heading" className="flex items-center gap-2 text-lg font-bold text-ink"><UserRound className="h-5 w-5 text-brand" /> Profile</h2>
              <p className="mt-1 text-sm text-muted">This is how other workspace members see you.</p>
            </div>
          </div>
          <form onSubmit={submit} aria-busy={isSubmitting} className="mt-6 grid max-w-xl gap-5">
            {error && <Alert>{error}</Alert>}
            {success && <Alert tone="success">Your profile has been updated.</Alert>}
            <TextField label="Username" value={user.username} disabled hint="Usernames cannot be changed." />
            <TextField label="Display name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} autoComplete="name" required maxLength={100} disabled={isSubmitting} />
            <TextField label="Email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="email" required maxLength={254} disabled={isSubmitting} />
            <div className="flex justify-end border-t border-line pt-5">
              <Button type="submit" disabled={isSubmitting || !displayName.trim() || !email.trim()}><Save className="h-4 w-4" /> {isSubmitting ? 'Saving…' : 'Save profile'}</Button>
            </div>
          </form>
        </section>
      </div>
    </div>
  )
}
