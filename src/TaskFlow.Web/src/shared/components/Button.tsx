import { forwardRef, type ButtonHTMLAttributes } from 'react'
import { cn } from '../lib/cn'

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'
type ButtonSize = 'sm' | 'md' | 'icon'

const variants: Record<ButtonVariant, string> = {
  primary: 'bg-accent text-white shadow-sm hover:brightness-95 active:brightness-90',
  secondary: 'border border-line-strong bg-surface text-ink shadow-sm hover:border-line-strong hover:bg-canvas',
  ghost: 'text-muted hover:bg-surface-muted hover:text-ink',
  danger: 'bg-red-600 text-white shadow-sm hover:bg-red-700',
}

const sizes: Record<ButtonSize, string> = {
  sm: 'min-h-9 rounded-lg px-3 text-sm',
  md: 'min-h-11 rounded-xl px-4 text-sm',
  icon: 'h-10 w-10 rounded-xl p-0',
}

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { className, variant = 'primary', size = 'md', type = 'button', ...props },
  ref,
) {
  return (
    <button
      ref={ref}
      type={type}
      className={cn(
        'inline-flex items-center justify-center gap-2 font-semibold transition disabled:cursor-not-allowed disabled:opacity-50',
        variants[variant],
        sizes[size],
        className,
      )}
      {...props}
    />
  )
})
