import { AlertCircle, CheckCircle2, Info } from 'lucide-react'
import { cn } from '../lib/cn'

type AlertTone = 'error' | 'success' | 'info'

const styles: Record<AlertTone, string> = {
  error: 'border-red-200 bg-red-50 text-red-900 dark:border-red-400/25 dark:bg-red-400/10 dark:text-red-200',
  success: 'border-emerald-200 bg-emerald-50 text-emerald-900 dark:border-emerald-400/25 dark:bg-emerald-400/10 dark:text-emerald-200',
  info: 'border-blue-200 bg-blue-50 text-blue-900 dark:border-blue-400/25 dark:bg-blue-400/10 dark:text-blue-200',
}

const icons = {
  error: AlertCircle,
  success: CheckCircle2,
  info: Info,
}

export function Alert({ children, tone = 'error', className }: { children: React.ReactNode; tone?: AlertTone; className?: string }) {
  const Icon = icons[tone]
  return (
    <div role={tone === 'error' ? 'alert' : 'status'} className={cn('flex items-start gap-3 rounded-xl border p-3.5 text-sm', styles[tone], className)}>
      <Icon aria-hidden="true" className="mt-0.5 h-4 w-4 shrink-0" />
      <div>{children}</div>
    </div>
  )
}
