import { useState } from 'react'
import { ArrowUpRight, Check, Circle, Layers3, LayoutGrid, MoreHorizontal, Users } from 'lucide-react'
import { Navigate, useNavigate } from 'react-router-dom'
import { Alert } from '../../../shared/components/Alert'
import { Button } from '../../../shared/components/Button'
import { LoginForm } from '../login/LoginForm'
import { RegisterForm } from '../register/RegisterForm'
import { useAuth } from '../session/useAuth'

type Mode = 'login' | 'register'

export function AuthPage() {
  const { user, acceptSession, sessionError, retrySession } = useAuth()
  const navigate = useNavigate()
  const [mode, setMode] = useState<Mode>('login')

  if (user) return <Navigate to="/" replace />

  const complete = (signedInUser: Parameters<typeof acceptSession>[0]) => {
    acceptSession(signedInUser)
    navigate('/', { replace: true })
  }

  return (
    <main className="grid flex-1 bg-canvas lg:grid-cols-[minmax(0,1fr)_minmax(460px,0.86fr)]">
      <section className="relative hidden overflow-hidden border-r border-line bg-[#edf1fb] px-10 py-10 dark:bg-[#202b42] lg:flex lg:flex-col lg:justify-between xl:px-16">
        <div aria-hidden="true" className="absolute -right-40 top-0 h-[36rem] w-[36rem] rounded-full border border-blue-400/10 bg-blue-400/5" />
        <div className="relative flex items-center gap-3 text-lg font-bold tracking-tight text-ink">
          <span className="grid h-10 w-10 place-items-center rounded-xl bg-accent text-white shadow-sm">
            <Check aria-hidden="true" className="h-5 w-5 stroke-[3]" />
          </span>
          TaskFlow
        </div>
        <div className="relative my-14 max-w-xl">
          <p className="flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.18em] text-brand"><span className="h-1.5 w-1.5 rounded-full bg-accent" /> A little clarity goes a long way</p>
          <h1 className="mt-5 max-w-lg text-5xl font-semibold leading-[1.1] tracking-[-0.04em] text-ink xl:text-6xl">Less scattered.<br />More done.</h1>
          <p className="mt-5 max-w-md text-base leading-7 text-muted">
            Bring your tasks, projects, and people into one place. Make room for the work that matters.
          </p>
          <div aria-hidden="true" className="mt-10 rounded-2xl border border-line bg-surface p-5 shadow-xl shadow-blue-950/5 dark:shadow-black/10">
            <div className="flex items-center justify-between border-b border-line pb-4">
              <div className="flex items-center gap-2.5 text-sm font-semibold text-ink"><span className="grid h-7 w-7 place-items-center rounded-lg bg-accent/10 text-brand"><LayoutGrid className="h-4 w-4" /></span> The next big thing</div>
              <MoreHorizontal className="h-4 w-4 text-faint" />
            </div>
            <div className="mt-4 grid grid-cols-3 gap-3">
              {[
                { title: 'To do', dot: 'bg-slate-400', task: 'Explore the idea', tag: 'Planning', color: 'bg-violet-50 text-violet-700 dark:bg-violet-400/10 dark:text-violet-300' },
                { title: 'In progress', dot: 'bg-blue-500', task: 'Make it happen', tag: 'Product', color: 'bg-blue-50 text-blue-700 dark:bg-blue-400/10 dark:text-blue-300' },
                { title: 'Done', dot: 'bg-emerald-500', task: 'Share the vision', tag: 'Team', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-400/10 dark:text-emerald-300' },
              ].map((column, index) => (
                <div key={column.title} className="min-w-0 rounded-xl bg-surface-muted p-2.5">
                  <div className="mb-3 flex items-center gap-1.5 text-[10px] font-semibold text-muted"><span className={`h-1.5 w-1.5 rounded-full ${column.dot}`} />{column.title}</div>
                  <div className="rounded-lg border border-line bg-surface p-2.5 shadow-sm">
                    <span className={`inline-block rounded px-1.5 py-0.5 text-[9px] font-semibold ${column.color}`}>{column.tag}</span>
                    <p className="mt-2 text-xs font-medium leading-5 text-ink">{column.task}</p>
                    <div className="mt-4 flex items-center justify-between">
                      <span className="grid h-5 w-5 place-items-center rounded-full bg-surface-muted text-[9px] font-semibold text-muted">{['AM', 'JD', 'KT'][index]}</span>
                      {index === 2 ? <Check className="h-3.5 w-3.5 text-emerald-600 dark:text-emerald-400" /> : <Circle className="h-3.5 w-3.5 text-faint" />}
                    </div>
                  </div>
                </div>
              ))}
            </div>
            <div className="mt-4 flex items-center gap-1.5 text-[11px] text-muted"><ArrowUpRight className="h-3.5 w-3.5 text-brand" /> One clear view of what comes next.</div>
          </div>
        </div>
        <div className="relative flex gap-7 text-xs font-medium text-muted"><span className="flex items-center gap-2"><Layers3 className="h-4 w-4 text-brand" /> Room for every project</span><span className="flex items-center gap-2"><Users className="h-4 w-4 text-brand" /> A place for your team</span></div>
      </section>

      <section className="flex items-center justify-center px-5 pb-12 pt-24 sm:px-10 lg:px-14 lg:py-20">
        <div className="w-full max-w-md">
          <div className="mb-8 flex items-center gap-3 lg:hidden">
            <span className="grid h-10 w-10 place-items-center rounded-xl bg-accent text-white"><Check className="h-5 w-5 stroke-[3]" /></span>
            <span className="text-xl font-bold">TaskFlow</span>
          </div>
          <p className="text-xs font-semibold uppercase tracking-[0.16em] text-brand">Welcome to TaskFlow</p>
          <h1 className="mt-3 text-3xl font-semibold tracking-tight text-ink">
            {mode === 'login' ? 'Sign in to your workspace' : 'Create your account'}
          </h1>
          <p className="mt-3 text-sm leading-6 text-muted">
            {mode === 'login' ? 'Pick up where your team left off.' : 'Start with a personal workspace, then invite your team.'}
          </p>

          <div className="my-7 grid grid-cols-2 rounded-xl border border-line bg-surface-muted p-1" role="tablist" aria-label="Account access">
            <Button role="tab" aria-selected={mode === 'login'} variant="ghost" size="sm" onClick={() => setMode('login')} className={mode === 'login' ? 'bg-surface text-ink shadow-sm hover:bg-surface' : ''}>Sign in</Button>
            <Button role="tab" aria-selected={mode === 'register'} variant="ghost" size="sm" onClick={() => setMode('register')} className={mode === 'register' ? 'bg-surface text-ink shadow-sm hover:bg-surface' : ''}>Register</Button>
          </div>

          {sessionError && (
            <Alert className="mb-5">
              <p>{sessionError}</p>
              <button type="button" className="mt-1 font-semibold underline" onClick={() => void retrySession()}>Try again</button>
            </Alert>
          )}
          {mode === 'login' ? <LoginForm onSuccess={complete} /> : <RegisterForm onSuccess={complete} />}
          <p className="mt-7 border-t border-line pt-5 text-xs leading-5 text-muted">
            Course demo fixture. Use sample data only, never personal or customer information.
          </p>
        </div>
      </section>
    </main>
  )
}
