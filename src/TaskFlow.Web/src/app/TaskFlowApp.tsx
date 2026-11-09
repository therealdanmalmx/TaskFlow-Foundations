import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { PasswordPage } from '../features/account/password/PasswordPage'
import { ProfilePage } from '../features/account/profile/ProfilePage'
import { AuthPage } from '../features/auth/access/AuthPage'
import { AuthProvider } from '../features/auth/session/AuthContext'
import { BoardPage } from '../features/boards/detail/BoardPage'
import { BoardsPage } from '../features/boards/list/BoardsPage'
import { MembersPage } from '../features/members/list/MembersPage'
import { CreateWorkspacePage } from '../features/workspaces/create/CreateWorkspacePage'
import { WorkspaceProvider } from '../features/workspaces/list/WorkspaceContext'
import { AppShell } from './AppShell'
import { HomeRedirect } from './HomeRedirect'
import { NotFoundPage } from './NotFoundPage'
import { PublicLayout } from './PublicLayout'
import { RequireAuth } from './RequireAuth'
import { ThemeProvider } from './theme/ThemeProvider'

export function TaskFlowApp({ deploymentLabel, sha }: { deploymentLabel: string; sha: string }) {
  return (
    <ThemeProvider><BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/auth" element={<PublicLayout deploymentLabel={deploymentLabel} sha={sha}><AuthPage /></PublicLayout>} />
          <Route element={<RequireAuth />}>
            <Route element={<WorkspaceProvider><AppShell deploymentLabel={deploymentLabel} sha={sha} /></WorkspaceProvider>}>
              <Route index element={<HomeRedirect />} />
              <Route path="workspaces/new" element={<CreateWorkspacePage />} />
              <Route path="workspaces/:workspaceId/boards" element={<BoardsPage />} />
              <Route path="workspaces/:workspaceId/boards/:boardId" element={<BoardPage />} />
              <Route path="workspaces/:workspaceId/members" element={<MembersPage />} />
              <Route path="settings/profile" element={<ProfilePage />} />
              <Route path="settings/security" element={<PasswordPage />} />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Route>
        </Routes>
      </AuthProvider>
    </BrowserRouter></ThemeProvider>
  )
}
