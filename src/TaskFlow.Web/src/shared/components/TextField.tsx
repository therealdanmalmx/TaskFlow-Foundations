import { forwardRef, type InputHTMLAttributes } from 'react'
import { cn } from '../lib/cn'

interface TextFieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string
  hint?: string
  error?: string
}

export const TextField = forwardRef<HTMLInputElement, TextFieldProps>(function TextField(
  { id, label, hint, error, className, ...props },
  ref,
) {
  const fieldId = id ?? `field-${label.toLowerCase().replaceAll(' ', '-')}`
  const descriptionId = hint || error ? `${fieldId}-description` : undefined

  return (
    <div className="grid gap-1.5 text-sm font-medium text-ink">
      <label htmlFor={fieldId}>{label}</label>
      <input
        ref={ref}
        id={fieldId}
        aria-invalid={Boolean(error)}
        aria-describedby={descriptionId}
        className={cn(
          'min-h-11 w-full rounded-xl border border-line-strong bg-surface px-3.5 text-ink shadow-sm transition placeholder:text-faint hover:border-line-strong focus:border-accent disabled:cursor-not-allowed disabled:bg-surface-muted',
          error && 'border-red-400',
          className,
        )}
        {...props}
      />
      {(error || hint) && (
        <span id={descriptionId} className={cn('text-xs font-normal text-muted', error && 'text-red-700 dark:text-red-300')}>
          {error ?? hint}
        </span>
      )}
    </div>
  )
})
