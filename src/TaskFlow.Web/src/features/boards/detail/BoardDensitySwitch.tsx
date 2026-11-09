import { LayoutList, Rows3 } from 'lucide-react'
import { cn } from '../../../shared/lib/cn'
import type { BoardDensity } from './useBoardDensity'

const options = [
  { value: 'compact', label: 'Compact', icon: Rows3 },
  { value: 'detailed', label: 'Detailed', icon: LayoutList },
] as const

interface BoardDensitySwitchProps {
  density: BoardDensity
  onChange: (density: BoardDensity) => void
  className?: string
}

export function BoardDensitySwitch({ density, onChange, className }: BoardDensitySwitchProps) {
  return (
    <div role="group" aria-label="Card view" className={cn('inline-flex shrink-0 items-center gap-1 rounded-xl border border-line bg-surface-muted p-1', className)}>
      {options.map(({ value, label, icon: Icon }) => (
        <button
          key={value}
          type="button"
          aria-pressed={density === value}
          onClick={() => onChange(value)}
          title={`${label} card view`}
          className={cn('inline-flex min-h-8 items-center justify-center gap-1.5 rounded-lg px-2.5 text-xs font-medium transition', density === value ? 'bg-surface-raised text-brand shadow-sm ring-1 ring-line' : 'text-muted hover:bg-surface hover:text-ink')}
        >
          <Icon aria-hidden="true" className="h-3.5 w-3.5" />
          {label}
        </button>
      ))}
    </div>
  )
}
