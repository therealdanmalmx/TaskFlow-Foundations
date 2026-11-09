import { forwardRef, type SelectHTMLAttributes } from 'react'
import { cn } from '../lib/cn'

interface SelectFieldProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label: string
}

export const SelectField = forwardRef<HTMLSelectElement, SelectFieldProps>(function SelectField(
  { id, label, className, children, ...props },
  ref,
) {
  const fieldId = id ?? `field-${label.toLowerCase().replaceAll(' ', '-')}`
  return (
    <label htmlFor={fieldId} className="grid gap-1.5 text-sm font-medium text-ink">
      {label}
      <select
        ref={ref}
        id={fieldId}
        className={cn(
          'min-h-11 w-full rounded-xl border border-line-strong bg-surface px-3.5 text-ink shadow-sm transition hover:border-line-strong focus:border-accent disabled:cursor-not-allowed disabled:bg-surface-muted',
          className,
        )}
        {...props}
      >
        {children}
      </select>
    </label>
  )
})
