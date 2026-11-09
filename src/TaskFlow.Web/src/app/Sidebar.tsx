import { Building2, Check, ChevronDown, LayoutDashboard, Plus, Settings, UsersRound, X } from 'lucide-react'
import { NavLink, useNavigate } from 'react-router-dom'
import type { User } from '../features/auth/model'
import type { Workspace } from '../features/workspaces/model'
import { Button } from '../shared/components/Button'
import { cn } from '../shared/lib/cn'
import { UserMenu } from './UserMenu'

interface SidebarProps {
  user: User
  workspaces: Workspace[]
  activeWorkspace: Workspace | null
  mobile?: boolean
  onClose?: () => void
  onLogout: () => Promise<void>
}

function navClass({ isActive }: { isActive: boolean }): string {
  return cn(
    'flex min-h-11 items-center gap-3 rounded-xl px-3 text-sm font-medium transition',
    isActive ? 'bg-accent/10 text-brand ring-1 ring-accent/10 dark:bg-accent/20 dark:ring-brand/15' : 'text-muted hover:bg-surface-muted hover:text-ink',
  )
}

export function Sidebar({ user, workspaces, activeWorkspace, mobile, onClose, onLogout }: SidebarProps) {
  const navigate = useNavigate()

  function switchWorkspace(value: string) {
    if (value === 'new') navigate('/workspaces/new')
    else navigate(`/workspaces/${value}/boards`)
    onClose?.()
  }

  return (
    <aside className="flex h-full w-72 flex-col border-r border-line bg-surface px-5 py-6">
      <div className="flex items-center gap-3 px-1">
        <NavLink to="/" className="flex min-w-0 flex-1 items-center gap-3 font-bold text-ink" onClick={onClose}>
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-[14px] bg-accent text-white shadow-lg shadow-accent/15 ring-1 ring-white/15 ring-inset">
            <Check aria-hidden="true" className="h-5 w-5 stroke-[3]" />
          </span>
          <span className="text-[22px] tracking-tight">Task<span className="font-medium text-brand">Flow</span></span>
        </NavLink>
        {mobile && <Button variant="ghost" size="icon" onClick={onClose} aria-label="Close navigation"><X className="h-5 w-5" /></Button>}
      </div>

      <div className="mt-9">
        <label htmlFor={mobile ? 'mobile-workspace-switcher' : 'workspace-switcher'} className="mb-2.5 block px-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-faint">Workspace</label>
        <div className="relative">
          <Building2 aria-hidden="true" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-brand" />
          <select
            id={mobile ? 'mobile-workspace-switcher' : 'workspace-switcher'}
            value={activeWorkspace?.id ?? ''}
            onChange={(event) => switchWorkspace(event.target.value)}
            className="min-h-12 w-full appearance-none rounded-xl border border-line bg-surface-muted/70 py-2 pl-10 pr-8 text-sm font-medium text-ink transition hover:border-line-strong focus:border-brand"
          >
            {!activeWorkspace && <option value="">Select workspace</option>}
            {workspaces.map((workspace) => <option key={workspace.id} value={workspace.id}>{workspace.name}</option>)}
            <option value="new">＋ New workspace</option>
          </select>
          <ChevronDown aria-hidden="true" className="pointer-events-none absolute right-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-faint" />
        </div>
      </div>

      <nav aria-label="Primary navigation" className="mt-6 grid gap-1.5">
        {activeWorkspace && (
          <>
            <NavLink to={`/workspaces/${activeWorkspace.id}/boards`} className={navClass} onClick={onClose}>
              <LayoutDashboard aria-hidden="true" className="h-4 w-4" /> Boards
            </NavLink>
            <NavLink to={`/workspaces/${activeWorkspace.id}/members`} className={navClass} onClick={onClose}>
              <UsersRound aria-hidden="true" className="h-4 w-4" /> Members
            </NavLink>
          </>
        )}
        <NavLink to="/settings/profile" className={navClass} onClick={onClose}>
          <Settings aria-hidden="true" className="h-4 w-4" /> Settings
        </NavLink>
      </nav>

      <Button variant="ghost" className="mt-4 justify-start text-muted" onClick={() => switchWorkspace('new')}>
        <Plus aria-hidden="true" className="h-4 w-4" /> New workspace
      </Button>

      <div className="mt-auto border-t border-line pt-4">
        <UserMenu user={user} onLogout={onLogout} />
      </div>
    </aside>
  )
}
