import { useEffect, useState } from 'react'
import { ChevronRight, Menu, PanelsTopLeft } from 'lucide-react'
import { Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../features/auth/session/useAuth'
import { useWorkspaces } from '../features/workspaces/list/useWorkspaces'
import { Alert } from '../shared/components/Alert'
import { Button } from '../shared/components/Button'
import { parseEntityId } from '../shared/lib/entityId'
import { DeploymentFooter } from './DeploymentFooter'
import { Sidebar } from './Sidebar'
import { ThemeSwitch } from './theme/ThemeSwitch'

function workspaceIdFromPath(pathname: string): string | null {
  return parseEntityId(/^\/workspaces\/([^/]+)/i.exec(pathname)?.[1])
}

export function AppShell({ deploymentLabel, sha }: { deploymentLabel: string; sha: string }) {
  const { user, logout, logoutError, clearLogoutError } = useAuth()
  const { workspaces, error, refresh } = useWorkspaces()
  const location = useLocation()
  const [mobileOpen, setMobileOpen] = useState(false)
  const workspaceId = workspaceIdFromPath(location.pathname)
  const activeWorkspace = workspaces.find((workspace) => workspace.id === workspaceId) ?? workspaces[0] ?? null

  useEffect(() => setMobileOpen(false), [location.pathname])

  if (!user) return null

  return (
    <div className="flex min-h-screen bg-canvas text-ink">
      <a href="#main-content" className="fixed left-3 top-3 z-[70] -translate-y-20 rounded-lg bg-slate-950 px-4 py-2 text-sm font-semibold text-white transition focus:translate-y-0">Skip to content</a>
      <div className="fixed inset-y-0 left-0 z-40 hidden lg:block">
        <Sidebar user={user} workspaces={workspaces} activeWorkspace={activeWorkspace} onLogout={logout} />
      </div>

      {mobileOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <button type="button" className="absolute inset-0 bg-slate-950/45 backdrop-blur-[2px]" onClick={() => setMobileOpen(false)} aria-label="Close navigation" />
          <div className="relative h-full w-72 shadow-2xl">
            <Sidebar mobile user={user} workspaces={workspaces} activeWorkspace={activeWorkspace} onClose={() => setMobileOpen(false)} onLogout={logout} />
          </div>
        </div>
      )}

      <div className="flex min-h-screen min-w-0 flex-1 flex-col lg:pl-72">
        <header className="sticky top-0 z-30 flex h-[72px] items-center justify-between gap-4 border-b border-line bg-surface/90 px-4 backdrop-blur-xl sm:px-6 lg:px-8">
          <div className="flex min-w-0 items-center gap-3 text-sm">
            <Button variant="ghost" size="icon" className="lg:hidden" onClick={() => setMobileOpen(true)} aria-label="Open navigation">
              <Menu aria-hidden="true" className="h-5 w-5" />
            </Button>
            <PanelsTopLeft aria-hidden="true" className="hidden h-4 w-4 shrink-0 text-faint lg:block" />
            <span className="truncate font-medium text-muted">{activeWorkspace?.name ?? 'TaskFlow'}</span>
            <ChevronRight aria-hidden="true" className="hidden h-3.5 w-3.5 shrink-0 text-faint sm:block" />
            <span className="hidden font-medium text-ink sm:block">{location.pathname.startsWith('/settings') ? 'Settings' : location.pathname.endsWith('/members') ? 'Members' : location.pathname.endsWith('/new') ? 'New workspace' : 'Boards'}</span>
          </div>
          <ThemeSwitch />
        </header>

        <main id="main-content" className="min-w-0 flex-1 px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
          {logoutError && (
            <Alert className="mx-auto mb-6 max-w-7xl">
              {logoutError}{' '}
              <button type="button" className="font-semibold underline" onClick={clearLogoutError}>Dismiss</button>
            </Alert>
          )}
          {error && location.pathname !== '/' && (
            <Alert className="mx-auto mb-6 max-w-7xl">
              {error}{' '}
              <button type="button" className="font-semibold underline" onClick={() => void refresh()}>Try again</button>
            </Alert>
          )}
          <Outlet />
        </main>
        <DeploymentFooter label={deploymentLabel} sha={sha} />
      </div>
    </div>
  )
}
