import { cn } from '../lib/cn'
import type { HTMLAttributes } from 'react'

type BadgeTone = 'slate' | 'blue' | 'amber' | 'emerald' | 'red' | 'purple'

const tones: Record<BadgeTone, string> = {
  slate: 'bg-surface-muted text-muted ring-line',
  blue: 'bg-blue-50 text-blue-700 ring-blue-600/10 dark:bg-blue-400/10 dark:text-blue-300 dark:ring-blue-400/20',
  amber: 'bg-amber-50 text-amber-800 ring-amber-600/10 dark:bg-amber-400/10 dark:text-amber-300 dark:ring-amber-400/20',
  emerald: 'bg-emerald-50 text-emerald-700 ring-emerald-600/10 dark:bg-emerald-400/10 dark:text-emerald-300 dark:ring-emerald-400/20',
  red: 'bg-red-50 text-red-700 ring-red-600/10 dark:bg-red-400/10 dark:text-red-300 dark:ring-red-400/20',
  purple: 'bg-purple-50 text-purple-700 ring-purple-600/10 dark:bg-purple-400/10 dark:text-purple-300 dark:ring-purple-400/20',
}

interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  tone?: BadgeTone
}

export function Badge({ children, tone = 'slate', className, ...props }: BadgeProps) {
  return <span className={cn('inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold ring-1 ring-inset', tones[tone], className)} {...props}>{children}</span>
}
