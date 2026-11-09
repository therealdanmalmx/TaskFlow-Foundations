import { Monitor, Moon, Sun } from 'lucide-react'
import { useContext } from 'react'
import { ThemeContext } from './theme-context'

const options = [
  { value: 'light', label: 'Light theme', icon: Sun },
  { value: 'dark', label: 'Dark theme', icon: Moon },
  { value: 'system', label: 'System theme', icon: Monitor },
] as const

export function ThemeSwitch() {
  const context = useContext(ThemeContext)
  if (!context) throw new Error('ThemeSwitch must be used within ThemeProvider')
  const { theme, setTheme } = context
  return (
    <div role="group" aria-label="Appearance" className="inline-flex shrink-0 items-center gap-1 rounded-xl border border-line bg-surface-muted p-1">
      {options.map(({ value, label, icon: Icon }) => (
        <button key={value} type="button" aria-label={label} title={label} aria-pressed={theme === value}
          onClick={() => setTheme(value)}
          className={`grid h-8 w-8 place-items-center rounded-lg transition ${theme === value ? 'bg-surface-raised text-brand shadow-sm ring-1 ring-line' : 'text-faint hover:bg-surface hover:text-ink'}`}>
          <Icon aria-hidden="true" className="h-4 w-4" />
        </button>
      ))}
    </div>
  )
}
