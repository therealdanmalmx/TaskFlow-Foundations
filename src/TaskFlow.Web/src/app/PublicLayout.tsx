import type { ReactNode } from 'react'
import { DeploymentFooter } from './DeploymentFooter'
import { ThemeSwitch } from './theme/ThemeSwitch'

export function PublicLayout({ children, deploymentLabel, sha }: { children: ReactNode; deploymentLabel: string; sha: string }) {
  return (
    <div className="relative flex min-h-screen flex-col bg-canvas text-ink">
      <div className="absolute right-4 top-4 z-20 sm:right-6 sm:top-6"><ThemeSwitch /></div>
      {children}
      <DeploymentFooter label={deploymentLabel} sha={sha} />
    </div>
  )
}
