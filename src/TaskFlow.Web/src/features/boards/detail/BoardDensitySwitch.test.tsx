import { act, cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { BoardDensitySwitch } from './BoardDensitySwitch'
import { useBoardDensity } from './useBoardDensity'

function CardViewPreference() {
  const { density, setDensity } = useBoardDensity()
  return <BoardDensitySwitch density={density} onChange={setDensity} />
}

function expectSelected(label: 'Compact' | 'Detailed') {
  expect(screen.getByRole('button', { name: label })).toHaveAttribute('aria-pressed', 'true')
  expect(screen.getByRole('button', { name: label === 'Compact' ? 'Detailed' : 'Compact' })).toHaveAttribute('aria-pressed', 'false')
}

beforeEach(() => localStorage.clear())
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  localStorage.clear()
})

describe('board card view preference', () => {
  it('defaults to detailed cards, persists either choice and restores it on the next visit', async () => {
    const user = userEvent.setup()
    const visit = render(<CardViewPreference />)

    expect(screen.getByRole('group', { name: 'Card view' })).toBeVisible()
    expectSelected('Detailed')

    await user.click(screen.getByRole('button', { name: 'Compact' }))
    expectSelected('Compact')
    expect(localStorage.getItem('taskflow-board-density')).toBe('compact')

    visit.unmount()
    render(<CardViewPreference />)
    expectSelected('Compact')

    await user.click(screen.getByRole('button', { name: 'Detailed' }))
    expectSelected('Detailed')
    expect(localStorage.getItem('taskflow-board-density')).toBe('detailed')
  })

  it('follows preference changes from another tab without reacting to unrelated settings', () => {
    render(<CardViewPreference />)

    act(() => {
      localStorage.setItem('taskflow-board-density', 'compact')
      window.dispatchEvent(new StorageEvent('storage', { key: 'taskflow-board-density', newValue: 'compact', storageArea: localStorage }))
    })
    expectSelected('Compact')

    act(() => {
      localStorage.setItem('taskflow-board-density', 'detailed')
      window.dispatchEvent(new StorageEvent('storage', { key: 'unrelated-preference', storageArea: localStorage }))
    })
    expectSelected('Compact')

    act(() => {
      localStorage.clear()
      window.dispatchEvent(new StorageEvent('storage', { key: null, storageArea: localStorage }))
    })
    expectSelected('Detailed')
  })

  it('keeps the view switch usable when browser storage is blocked', async () => {
    const user = userEvent.setup()
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('Storage is unavailable', 'SecurityError') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Storage is unavailable', 'SecurityError') })
    render(<CardViewPreference />)

    expectSelected('Detailed')
    await user.click(screen.getByRole('button', { name: 'Compact' }))
    expectSelected('Compact')
    await user.click(screen.getByRole('button', { name: 'Detailed' }))
    expectSelected('Detailed')
  })

  it('uses the default view when a saved value is invalid', () => {
    localStorage.setItem('taskflow-board-density', 'obsolete-view')
    render(<CardViewPreference />)
    expectSelected('Detailed')
  })
})
