import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { LogOut, Search, ShieldCheck, Trash2, UserPlus, UsersRound } from 'lucide-react'
import { useNavigate, useParams } from 'react-router-dom'
import { NotFoundPage } from '../../../app/NotFoundPage'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Avatar } from '../../../shared/components/Avatar'
import { Badge } from '../../../shared/components/Badge'
import { Button } from '../../../shared/components/Button'
import { ConfirmDialog } from '../../../shared/components/ConfirmDialog'
import { EmptyState } from '../../../shared/components/EmptyState'
import { LoadingState } from '../../../shared/components/LoadingState'
import { formatDate } from '../../../shared/lib/date'
import { parseEntityId } from '../../../shared/lib/entityId'
import { AddMemberDialog } from '../add/AddMemberDialog'
import { changeMemberRole } from '../change-role/api'
import type { Member } from '../model'
import { removeMember } from '../remove/api'
import { canManageWorkspace, type WorkspaceRole } from '../../workspaces/model'
import { useWorkspaces } from '../../workspaces/list/useWorkspaces'
import { getMembers } from './api'
import { useAuth } from '../../auth/session/useAuth'

const roleTone: Record<WorkspaceRole, 'purple' | 'blue' | 'emerald'> = {
  Owner: 'purple',
  Admin: 'blue',
  Member: 'emerald',
}

export function MembersPage() {
  const { workspaceId: workspaceIdParam } = useParams()
  const workspaceId = parseEntityId(workspaceIdParam)

  if (!workspaceId) return <NotFoundPage />

  return <MembersPageContent key={workspaceId} workspaceId={workspaceId} />
}

function MembersPageContent({ workspaceId }: { workspaceId: string }) {
  const { user } = useAuth()
  const navigate = useNavigate()
  const { workspaces, refresh } = useWorkspaces()
  const workspace = workspaces.find((item) => item.id === workspaceId)
  const canManage = workspace ? canManageWorkspace(workspace.role) : false
  const [members, setMembers] = useState<Member[]>([])
  const [search, setSearch] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [addOpen, setAddOpen] = useState(false)
  const [removeCandidate, setRemoveCandidate] = useState<Member | null>(null)
  const [removeError, setRemoveError] = useState<string | null>(null)
  const [pendingUserIds, setPendingUserIds] = useState<Set<string>>(new Set())
  const activeRef = useRef(true)
  const requestControllerRef = useRef<AbortController | null>(null)
  const isCurrentRoute = useCallback(() => activeRef.current && window.location.pathname.replace(/\/+$/, '').toLowerCase() === `/workspaces/${workspaceId}/members`, [workspaceId])

  const load = useCallback(async () => {
    requestControllerRef.current?.abort()
    const controller = new AbortController()
    requestControllerRef.current = controller
    setIsLoading(true)
    setError(null)
    try {
      const loadedMembers = await getMembers(workspaceId, controller.signal)
      if (isCurrentRoute() && !controller.signal.aborted) setMembers(loadedMembers)
    } catch (requestError) {
      if (!isCurrentRoute() || controller.signal.aborted) return
      setError(apiErrorMessage(requestError, 'Could not load workspace members.'))
    } finally {
      if (isCurrentRoute() && !controller.signal.aborted) setIsLoading(false)
    }
  }, [isCurrentRoute, workspaceId])

  useEffect(() => {
    activeRef.current = true
    void load()
    return () => {
      activeRef.current = false
      requestControllerRef.current?.abort()
    }
  }, [load])

  function setPending(userId: string, pending: boolean) {
    setPendingUserIds((current) => {
      const next = new Set(current)
      if (pending) next.add(userId)
      else next.delete(userId)
      return next
    })
  }

  async function changeRole(member: Member, role: WorkspaceRole) {
    if (!isCurrentRoute()) return
    setPending(member.userId, true)
    setError(null)
    try {
      const updated = await changeMemberRole(workspaceId, member.userId, role)
      if (!isCurrentRoute()) return
      setMembers((current) => current.map((item) => item.userId === updated.userId ? updated : item))
      if (updated.userId === user?.id) await refresh()
    } catch (requestError) {
      if (!isCurrentRoute()) return
      setError(apiErrorMessage(requestError, 'Could not change the member role.'))
    } finally {
      if (isCurrentRoute()) setPending(member.userId, false)
    }
  }

  async function confirmRemove() {
    const candidate = removeCandidate
    if (!candidate || !isCurrentRoute()) return
    const removingSelf = candidate.userId === user?.id
    setPending(candidate.userId, true)
    setRemoveError(null)
    try {
      await removeMember(workspaceId, candidate.userId)
      if (!isCurrentRoute()) return
      setMembers((current) => current.filter((item) => item.userId !== candidate.userId))
      setRemoveCandidate(null)
      if (removingSelf) {
        await refresh()
        if (isCurrentRoute()) navigate('/', { replace: true })
      }
    } catch (requestError) {
      if (!isCurrentRoute()) return
      setRemoveError(apiErrorMessage(requestError, 'Could not remove the member.'))
    } finally {
      if (isCurrentRoute()) setPending(candidate.userId, false)
    }
  }

  const visibleMembers = useMemo(() => {
    const query = search.trim().toLocaleLowerCase()
    return members.filter((member) => !query || `${member.displayName} ${member.username} ${member.email} ${member.role}`.toLocaleLowerCase().includes(query))
  }, [members, search])

  if (isLoading) return <LoadingState label="Loading members…" />

  return (
    <div className="mx-auto max-w-6xl">
      <header className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="text-sm font-bold uppercase tracking-[0.14em] text-brand">{workspace?.name ?? 'Workspace'}</p>
          <div className="mt-2 flex items-center gap-3"><h1 className="text-3xl font-semibold tracking-tight text-ink">Members</h1><span aria-label={`${members.length} members`} className="rounded-lg border border-line bg-surface px-2.5 py-1 text-sm font-medium tabular-nums text-muted">{members.length}</span></div>
          <p className="mt-2 text-sm text-muted">Manage who can see and change work in this workspace.</p>
        </div>
        {canManage && <Button onClick={() => setAddOpen(true)}><UserPlus className="h-4 w-4" /> Add member</Button>}
      </header>

      {!canManage && <Alert tone="info" className="mt-6"><ShieldCheck className="mr-1 inline h-4 w-4" /> You can view members and leave this workspace. Only owners and admins can change another member’s access.</Alert>}
      {error && <Alert className="mt-6">{error}</Alert>}

      <label className="relative mt-7 block max-w-md">
        <span className="sr-only">Search members</span>
        <Search aria-hidden="true" className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-faint" />
        <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search members…" className="min-h-11 w-full rounded-xl border border-line bg-surface pl-9 pr-3 text-sm text-ink shadow-sm placeholder:text-faint focus:border-accent" />
      </label>

      {visibleMembers.length === 0 ? (
        <div className="mt-6"><EmptyState icon={UsersRound} title={search ? 'No members match' : 'No members yet'} description={search ? 'Try a different name, username, or role.' : 'Add a member to start collaborating.'} /></div>
      ) : (
        <div className="mt-6 overflow-hidden rounded-2xl border border-line bg-surface shadow-sm">
          <ul className="divide-y divide-line">
            {visibleMembers.map((member) => {
              const pending = pendingUserIds.has(member.userId)
              const isSelf = member.userId === user?.id
              const canManageTarget = canManage && !(workspace?.role === 'Admin' && member.role === 'Owner')
              return (
                <li key={member.userId} aria-busy={pending} className="flex flex-col gap-4 p-5 transition-colors hover:bg-surface-muted/50 sm:flex-row sm:items-center sm:px-6">
                  <Avatar name={member.displayName} size="lg" />
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <h2 className="truncate font-semibold text-ink">{member.displayName}</h2>
                      {isSelf && <span className="text-xs text-faint">you</span>}
                      <Badge tone={roleTone[member.role]}>{member.role}</Badge>
                    </div>
                    <p className="mt-0.5 truncate text-sm text-muted">@{member.username} · {member.email}</p>
                    <p className="mt-1 text-xs text-faint">Joined {formatDate(member.joinedAt)}</p>
                  </div>
                  {(canManageTarget || isSelf) && (
                    <div className="flex items-center gap-2">
                      {canManageTarget && <label>
                        <span className="sr-only">Role for {member.displayName}</span>
                        <select
                          value={member.role}
                          disabled={pending}
                          onChange={(event) => void changeRole(member, event.target.value as WorkspaceRole)}
                          className="min-h-10 rounded-xl border border-line bg-surface px-3 text-sm font-medium text-ink disabled:bg-surface-muted disabled:text-muted"
                        >
                          {workspace?.role === 'Owner' && <option value="Owner">Owner</option>}
                          <option value="Admin">Admin</option>
                          <option value="Member">Member</option>
                        </select>
                      </label>}
                      <Button variant={isSelf ? 'secondary' : 'ghost'} size={isSelf ? 'sm' : 'icon'} className="text-muted hover:border-red-200 hover:bg-red-50 hover:text-red-700 dark:hover:border-red-400/25 dark:hover:bg-red-400/10 dark:hover:text-red-300" disabled={pending} onClick={() => { setRemoveError(null); setRemoveCandidate(member) }} aria-label={isSelf ? `Leave ${workspace?.name ?? 'workspace'}` : `Remove ${member.displayName}`}>
                        {isSelf ? <><LogOut aria-hidden="true" className="h-4 w-4" /> Leave workspace</> : <Trash2 aria-hidden="true" className="h-4 w-4" />}
                      </Button>
                    </div>
                  )}
                </li>
              )
            })}
          </ul>
        </div>
      )}

      <AddMemberDialog open={addOpen} workspaceId={workspaceId} canAssignOwner={workspace?.role === 'Owner'} onClose={() => setAddOpen(false)} onAdded={(member) => { if (!isCurrentRoute()) return; setMembers((current) => [...current, member]); setAddOpen(false) }} />
      <ConfirmDialog
        open={removeCandidate !== null}
        title={removeCandidate?.userId === user?.id ? 'Leave workspace?' : 'Remove workspace member?'}
        description={removeCandidate?.userId === user?.id
          ? `You will immediately lose access to every board in ${workspace?.name ?? 'this workspace'}. Your existing tasks will remain.`
          : `${removeCandidate?.displayName ?? 'This member'} will immediately lose access to every board in ${workspace?.name ?? 'this workspace'}. Their existing tasks will remain.`}
        confirmLabel={removeCandidate?.userId === user?.id ? 'Leave workspace' : 'Remove member'}
        error={removeError}
        isPending={removeCandidate ? pendingUserIds.has(removeCandidate.userId) : false}
        onClose={() => { setRemoveCandidate(null); setRemoveError(null) }}
        onConfirm={() => void confirmRemove()}
      />
    </div>
  )
}
