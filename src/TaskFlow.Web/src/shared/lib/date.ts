export function formatDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  }).format(new Date(value))
}

export function formatShortDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, {
    day: 'numeric',
    month: 'short',
  }).format(parseDateOnly(value))
}

export function parseDateOnly(value: string): Date {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (!match) return new Date(value)
  return new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]))
}

export function relativeTime(value: string): string {
  const elapsedSeconds = Math.round((Date.now() - Date.parse(value)) / 1_000)
  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })

  if (Math.abs(elapsedSeconds) < 60) return formatter.format(-elapsedSeconds, 'second')
  const minutes = Math.round(elapsedSeconds / 60)
  if (Math.abs(minutes) < 60) return formatter.format(-minutes, 'minute')
  const hours = Math.round(minutes / 60)
  if (Math.abs(hours) < 24) return formatter.format(-hours, 'hour')
  return formatter.format(-Math.round(hours / 24), 'day')
}

export function isOverdue(value: string | null, completed: boolean): boolean {
  if (!value || completed) return false
  const endOfDueDate = parseDateOnly(value.slice(0, 10))
  endOfDueDate.setHours(23, 59, 59, 999)
  return endOfDueDate.getTime() < Date.now()
}
