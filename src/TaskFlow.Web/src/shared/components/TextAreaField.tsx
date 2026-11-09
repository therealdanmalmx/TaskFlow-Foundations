import { forwardRef, type TextareaHTMLAttributes } from 'react'
import { cn } from '../lib/cn'

interface TextAreaFieldProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label: string
  hint?: string
}

export const TextAreaField = forwardRef<HTMLTextAreaElement, TextAreaFieldProps>(function TextAreaField(
  { id, label, hint, className, ...props },
  ref,
) {
  const fieldId = id ?? `field-${label.toLowerCase().replaceAll(' ', '-')}`
  return (
    <div className="grid gap-1.5 text-sm font-medium text-ink">
      <label htmlFor={fieldId}>{label}</label>
      <textarea
        ref={ref}
        id={fieldId}
        className={cn(
          'min-h-24 w-full resize-y rounded-xl border border-line-strong bg-surface px-3.5 py-2.5 text-ink shadow-sm transition placeholder:text-faint hover:border-line-strong focus:border-accent disabled:cursor-not-allowed disabled:bg-surface-muted',
          className,
        )}
        {...props}
      />
      {hint && <span className="text-xs font-normal text-muted">{hint}</span>}
    </div>
  )
})
