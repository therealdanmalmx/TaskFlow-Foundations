import { useEffect, useRef, useState } from 'react'
import { ChevronDown, KeyRound, LogOut, UserRound } from 'lucide-react'
import { Link, useNavigate } from 'react-router-dom'
import type { User } from '../features/auth/model'
import { Avatar } from '../shared/components/Avatar'
import { Button } from '../shared/components/Button'

export function UserMenu({ user, onLogout }: { user: User; onLogout: () => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)
  const navigate = useNavigate()

  useEffect(() => {
    if (!open) return
    const close = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) setOpen(false)
    }
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', closeOnEscape)
    return () => {
      document.removeEventListener('mousedown', close)
      document.removeEventListener('keydown', closeOnEscape)
    }
  }, [open])

  async function logout() {
    setOpen(false)
    try {
      await onLogout()
      navigate('/auth', { replace: true })
    } catch {
      // AuthProvider reconciles the cookie and exposes a visible status message.
    }
  }

  return (
    <div ref={containerRef} className="relative">
      <Button
        variant="ghost"
        className="h-auto w-full justify-start px-2 py-2 text-left"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <Avatar name={user.displayName} />
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-semibold text-ink">{user.displayName}</span>
          <span className="block truncate text-xs font-normal text-muted">@{user.username}</span>
        </span>
        <ChevronDown aria-hidden="true" className="h-4 w-4 text-faint" />
      </Button>

      {open && (
        <div role="menu" className="surface-enter absolute bottom-[calc(100%+0.5rem)] left-0 z-50 w-full min-w-56 rounded-xl border border-line bg-surface p-1.5 shadow-xl shadow-slate-900/10">
          <Link role="menuitem" to="/settings/profile" onClick={() => setOpen(false)} className="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-ink hover:bg-surface-muted hover:text-ink">
            <UserRound aria-hidden="true" className="h-4 w-4" /> Profile settings
          </Link>
          <Link role="menuitem" to="/settings/security" onClick={() => setOpen(false)} className="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-ink hover:bg-surface-muted hover:text-ink">
            <KeyRound aria-hidden="true" className="h-4 w-4" /> Password & security
          </Link>
          <div className="my-1 border-t border-line" />
          <button role="menuitem" type="button" title="Ends all active TaskFlow sessions for this account" onClick={() => void logout()} className="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm text-red-700 dark:text-red-300 hover:bg-red-50 dark:hover:bg-red-400/10">
            <LogOut aria-hidden="true" className="h-4 w-4" /> Log out everywhere
          </button>
        </div>
      )}
    </div>
  )
}
