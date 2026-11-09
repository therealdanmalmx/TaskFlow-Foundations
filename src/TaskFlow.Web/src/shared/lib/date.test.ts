import { afterEach, describe, expect, it, vi } from 'vitest'
import { formatShortDate, isOverdue, parseDateOnly } from './date'

afterEach(() => vi.useRealTimers())

describe('date-only values', () => {
  it('parses a due date at local midnight without a UTC day shift', () => {
    const parsed = parseDateOnly('2026-08-29')

    expect(parsed.getFullYear()).toBe(2026)
    expect(parsed.getMonth()).toBe(7)
    expect(parsed.getDate()).toBe(29)
    expect(parsed.getHours()).toBe(0)
    expect(formatShortDate('2026-08-29')).toBe(new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short' }).format(new Date(2026, 7, 29)))
  })

  it('keeps a due date active through the end of the local calendar day', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 7, 29, 12))

    expect(isOverdue('2026-08-29', false)).toBe(false)
    expect(isOverdue('2026-08-28', false)).toBe(true)
    expect(isOverdue('2026-08-28', true)).toBe(false)
  })
})
