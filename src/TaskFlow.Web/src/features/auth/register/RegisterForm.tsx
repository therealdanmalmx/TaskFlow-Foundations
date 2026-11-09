import { useState, type FormEvent } from 'react'
import { ArrowRight } from 'lucide-react'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Button } from '../../../shared/components/Button'
import { TextField } from '../../../shared/components/TextField'
import type { User } from '../model'
import { register } from './api'

export function RegisterForm({ onSuccess }: { onSuccess: (user: User) => void }) {
  const [displayName, setDisplayName] = useState('')
  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (isSubmitting) return
    setIsSubmitting(true)
    setError(null)
    try {
      const response = await register({
        displayName: displayName.trim(),
        username: username.trim(),
        email: email.trim(),
        password,
      })
      onSuccess(response.user)
    } catch (requestError) {
      setError(apiErrorMessage(requestError, 'Could not create your account.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <form onSubmit={submit} aria-busy={isSubmitting} className="grid gap-4">
      {error && <Alert>{error}</Alert>}
      <TextField
        label="Display name"
        value={displayName}
        onChange={(event) => setDisplayName(event.target.value)}
        autoComplete="name"
        placeholder="Alex Morgan"
        required
        maxLength={100}
        disabled={isSubmitting}
      />
      <TextField
        label="Username"
        value={username}
        onChange={(event) => setUsername(event.target.value)}
        autoComplete="username"
        placeholder="alex-morgan"
        required
        minLength={3}
        maxLength={50}
        pattern="[A-Za-z0-9._-]+"
        hint="Letters, numbers, dots, dashes, and underscores."
        disabled={isSubmitting}
      />
      <TextField
        label="Email"
        type="email"
        value={email}
        onChange={(event) => setEmail(event.target.value)}
        autoComplete="email"
        placeholder="alex@example.com"
        required
        maxLength={254}
        disabled={isSubmitting}
      />
      <TextField
        label="Password"
        type="password"
        value={password}
        onChange={(event) => setPassword(event.target.value)}
        autoComplete="new-password"
        placeholder="At least 12 characters"
        required
        minLength={12}
        maxLength={200}
        hint="Use 12 or more characters you do not use elsewhere."
        disabled={isSubmitting}
      />
      <Button type="submit" disabled={isSubmitting || !displayName.trim() || !username.trim() || !email.trim() || password.length < 12} className="mt-1 w-full">
        {isSubmitting ? 'Creating account…' : 'Create account'}
        {!isSubmitting && <ArrowRight aria-hidden="true" className="h-4 w-4" />}
      </Button>
    </form>
  )
}
