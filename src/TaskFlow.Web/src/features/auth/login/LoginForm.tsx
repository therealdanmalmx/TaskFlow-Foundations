import { useState, type FormEvent } from 'react'
import { ArrowRight } from 'lucide-react'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Button } from '../../../shared/components/Button'
import { TextField } from '../../../shared/components/TextField'
import type { User } from '../model'
import { login } from './api'

export function LoginForm({ onSuccess }: { onSuccess: (user: User) => void }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (isSubmitting) return
    setIsSubmitting(true)
    setError(null)
    try {
      const response = await login({ username: username.trim(), password })
      onSuccess(response.user)
    } catch (requestError) {
      setError(apiErrorMessage(requestError, 'Could not sign in. The API may be restarting.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <form onSubmit={submit} aria-busy={isSubmitting} className="grid gap-4">
      {error && <Alert>{error}</Alert>}
      <TextField
        label="Username"
        value={username}
        onChange={(event) => setUsername(event.target.value)}
        autoComplete="username"
        placeholder="your-username"
        required
        maxLength={50}
        disabled={isSubmitting}
      />
      <TextField
        label="Password"
        type="password"
        value={password}
        onChange={(event) => setPassword(event.target.value)}
        autoComplete="current-password"
        placeholder="Enter your password"
        required
        maxLength={200}
        disabled={isSubmitting}
      />
      <Button type="submit" disabled={isSubmitting || !username.trim() || !password} className="mt-1 w-full">
        {isSubmitting ? 'Signing in…' : 'Sign in'}
        {!isSubmitting && <ArrowRight aria-hidden="true" className="h-4 w-4" />}
      </Button>
    </form>
  )
}
