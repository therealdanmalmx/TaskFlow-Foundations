import { KeyRound, UserRound } from 'lucide-react'
import { NavLink } from 'react-router-dom'
import { cn } from '../../shared/lib/cn'

const links = [
  { to: '/settings/profile', label: 'Profile', icon: UserRound },
  { to: '/settings/security', label: 'Password & security', icon: KeyRound },
]

export function SettingsNavigation() {
  return (
    <nav aria-label="Settings navigation" className="flex gap-1 self-start overflow-x-auto border-b border-line lg:grid lg:rounded-2xl lg:border lg:bg-surface lg:p-2">
      {links.map(({ to, label, icon: Icon }) => (
        <NavLink key={to} to={to} className={({ isActive }) => cn('flex min-h-11 shrink-0 items-center gap-2 border-b-2 px-3 text-sm font-semibold transition lg:rounded-xl lg:border-0', isActive ? 'border-accent text-brand lg:bg-accent/10' : 'border-transparent text-muted hover:text-ink lg:hover:bg-surface-muted')}>
          <Icon aria-hidden="true" className="h-4 w-4" /> {label}
        </NavLink>
      ))}
    </nav>
  )
}
