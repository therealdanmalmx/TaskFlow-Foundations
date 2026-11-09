import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Archive, LayoutDashboard, Plus, RefreshCw } from 'lucide-react'
import { useNavigate, useParams } from 'react-router-dom'
import { apiErrorMessage } from '../../../shared/api/HttpClient'
import { Alert } from '../../../shared/components/Alert'
import { Button } from '../../../shared/components/Button'
import { EmptyState } from '../../../shared/components/EmptyState'
import { LoadingState } from '../../../shared/components/LoadingState'
import { NotFoundPage } from '../../../app/NotFoundPage'
import { cn } from '../../../shared/lib/cn'
import { parseEntityId } from '../../../shared/lib/entityId'
import { canManageWorkspace } from '../../workspaces/model'
import { useWorkspaces } from '../../workspaces/list/useWorkspaces'
import { CreateBoardDialog } from '../create/CreateBoardDialog'
import type { BoardSummary } from '../model'
import { BoardCard } from './BoardCard'
import { getBoards } from './api'

type BoardFilter = 'active' | 'archived'

export function BoardsPage() {
  const { workspaceId: workspaceIdParam } = useParams()
  const workspaceId = parseEntityId(workspaceIdParam)

  if (!workspaceId) return <NotFoundPage />

  return <BoardsPageContent key={workspaceId} workspaceId={workspaceId} />
}

function BoardsPageContent({ workspaceId }: { workspaceId: string }) {
  const { workspaces } = useWorkspaces()
  const navigate = useNavigate()
  const workspace = workspaces.find((item) => item.id === workspaceId)
  const [boards, setBoards] = useState<BoardSummary[]>([])
  const [filter, setFilter] = useState<BoardFilter>('active')
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const activeRef = useRef(true)
  const requestControllerRef = useRef<AbortController | null>(null)
  const isCurrentRoute = useCallback(() => activeRef.current && window.location.pathname.replace(/\/+$/, '').toLowerCase() === `/workspaces/${workspaceId}/boards`, [workspaceId])

  const load = useCallback(async () => {
    requestControllerRef.current?.abort()
    const controller = new AbortController()
    requestControllerRef.current = controller
    setIsLoading(true)
    setError(null)
    try {
      const loadedBoards = await getBoards(workspaceId, controller.signal)
      if (isCurrentRoute() && !controller.signal.aborted) setBoards(loadedBoards)
    } catch (requestError) {
      if (!isCurrentRoute() || controller.signal.aborted) return
      setError(apiErrorMessage(requestError, 'Could not load the boards.'))
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

  const visibleBoards = useMemo(() => boards.filter((board) => board.isArchived === (filter === 'archived')), [boards, filter])
  const canCreate = workspace ? canManageWorkspace(workspace.role) : false

  if (isLoading) return <LoadingState label="Loading boards…" />

  return (
    <div className="mx-auto max-w-7xl">
      <header className="flex flex-col gap-5 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <p className="mb-3 inline-flex items-center gap-2 text-xs font-medium text-muted"><span className="h-1.5 w-1.5 rounded-full bg-accent" aria-hidden="true" />{workspace?.name ?? 'Workspace'}</p>
          <h1 className="text-3xl font-semibold tracking-tight text-ink">Boards</h1>
          <p className="mt-2 text-sm leading-6 text-muted">A little structure for your next big idea.</p>
        </div>
        {canCreate && <Button onClick={() => setCreateOpen(true)}><Plus aria-hidden="true" className="h-4 w-4" /> New board</Button>}
      </header>

      <div className="mt-8 flex items-center gap-5 border-b border-line" role="tablist" aria-label="Board status">
        {(['active', 'archived'] as BoardFilter[]).map((value) => (
          <button
            key={value}
            type="button"
            role="tab"
            aria-selected={filter === value}
            onClick={() => setFilter(value)}
            className={cn('-mb-px flex items-center gap-2 border-b-2 px-1 pb-3 text-sm font-semibold capitalize transition', filter === value ? 'border-accent text-brand' : 'border-transparent text-muted hover:text-ink')}
          >
            {value}
            <span className={cn('rounded-md px-1.5 py-0.5 text-[10px] tabular-nums', filter === value ? 'bg-accent/10 text-brand' : 'bg-surface-muted text-muted')}>{boards.filter((board) => board.isArchived === (value === 'archived')).length}</span>
          </button>
        ))}
      </div>

      {error && (
        <Alert className="mt-6">
          <span>{error}</span>
          <button type="button" className="ml-2 inline-flex items-center gap-1 font-semibold underline" onClick={() => void load()}><RefreshCw className="h-3.5 w-3.5" /> Retry</button>
        </Alert>
      )}

      {!error && visibleBoards.length > 0 && (
        <div className="mt-6 grid gap-5 md:grid-cols-2 2xl:grid-cols-3">
          {visibleBoards.map((board) => <BoardCard key={board.id} board={board} />)}
        </div>
      )}

      {!error && visibleBoards.length === 0 && (
        <div className="mt-6">
          <EmptyState
            icon={filter === 'archived' ? Archive : LayoutDashboard}
            title={filter === 'archived' ? 'No archived boards' : 'Create your first board'}
            description={filter === 'archived' ? 'Boards you archive will stay available here.' : canCreate ? 'Start with one outcome or stream of work. You can add the details as you go.' : 'An owner or admin can create the first board for this workspace.'}
            action={canCreate && filter === 'active' ? <Button onClick={() => setCreateOpen(true)}><Plus className="h-4 w-4" /> Create board</Button> : undefined}
          />
        </div>
      )}

      <CreateBoardDialog
        open={createOpen}
        workspaceId={workspaceId}
        onClose={() => setCreateOpen(false)}
        onCreated={(board) => {
          if (!isCurrentRoute() || board.workspaceId !== workspaceId) return
          setBoards((current) => [...current, board])
          setCreateOpen(false)
          navigate(`/workspaces/${workspaceId}/boards/${board.id}`)
        }}
      />
    </div>
  )
}
