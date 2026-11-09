import { act, cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ThemeProvider } from './ThemeProvider'
import { ThemeSwitch } from './ThemeSwitch'

let systemDark = false
let mediaEvents: EventTarget

function renderAppearance() {
  return render(<ThemeProvider><ThemeSwitch /></ThemeProvider>)
}

function changeSystemTheme(dark: boolean) {
  act(() => {
    systemDark = dark
    mediaEvents.dispatchEvent(new Event('change'))
  })
}

function expectSelected(label: string) {
  for (const button of screen.getAllByRole('button')) {
    expect(button).toHaveAttribute('aria-pressed', button.getAttribute('aria-label') === label ? 'true' : 'false')
  }
}

beforeEach(() => {
  localStorage.clear()
  systemDark = false
  mediaEvents = new EventTarget()
  vi.stubGlobal('matchMedia', vi.fn(() => ({
    get matches() { return systemDark },
    media: '(prefers-color-scheme: dark)',
    addEventListener: mediaEvents.addEventListener.bind(mediaEvents),
    removeEventListener: mediaEvents.removeEventListener.bind(mediaEvents),
  })))
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  localStorage.clear()
  delete document.documentElement.dataset.theme
})

describe('appearance preferences', () => {
  it('defaults to the system preference and follows changes in either direction', () => {
    renderAppearance()

    expect(screen.getByRole('group', { name: 'Appearance' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Light theme' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Dark theme' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'System theme' })).toBeVisible()
    expectSelected('System theme')
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')

    changeSystemTheme(true)
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
    changeSystemTheme(false)
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')
    expectSelected('System theme')
  })

  it('restores a saved choice, persists user changes and ignores the system until System is selected', async () => {
    const user = userEvent.setup()
    localStorage.setItem('taskflow-theme', 'dark')
    const firstVisit = renderAppearance()

    expectSelected('Dark theme')
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
    changeSystemTheme(true)
    changeSystemTheme(false)
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')

    await user.click(screen.getByRole('button', { name: 'Light theme' }))
    expect(localStorage.getItem('taskflow-theme')).toBe('light')
    changeSystemTheme(true)
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')
    expectSelected('Light theme')

    firstVisit.unmount()
    renderAppearance()
    expectSelected('Light theme')
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')

    await user.click(screen.getByRole('button', { name: 'System theme' }))
    expectSelected('System theme')
    expect(localStorage.getItem('taskflow-theme')).toBe('system')
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
    changeSystemTheme(false)
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')

    await user.click(screen.getByRole('button', { name: 'Dark theme' }))
    expectSelected('Dark theme')
    expect(localStorage.getItem('taskflow-theme')).toBe('dark')
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
  })

  it('updates an open tab when another tab changes or clears the saved preference', () => {
    renderAppearance()

    act(() => {
      localStorage.setItem('taskflow-theme', 'dark')
      window.dispatchEvent(new StorageEvent('storage', { key: 'taskflow-theme', newValue: 'dark', storageArea: localStorage }))
    })
    expectSelected('Dark theme')
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')

    act(() => {
      localStorage.setItem('taskflow-theme', 'light')
      window.dispatchEvent(new StorageEvent('storage', { key: 'some-other-setting', newValue: 'example', storageArea: localStorage }))
    })
    expectSelected('Dark theme')
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')

    act(() => {
      localStorage.clear()
      window.dispatchEvent(new StorageEvent('storage', { key: null, storageArea: localStorage }))
    })
    expectSelected('System theme')
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')
    changeSystemTheme(true)
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
  })

  it('keeps all appearance controls usable when browser storage is unavailable', async () => {
    const user = userEvent.setup()
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('Storage is unavailable', 'SecurityError') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Storage is unavailable', 'SecurityError') })
    systemDark = true
    renderAppearance()

    expectSelected('System theme')
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
    await user.click(screen.getByRole('button', { name: 'Light theme' }))
    expectSelected('Light theme')
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')
    await user.click(screen.getByRole('button', { name: 'Dark theme' }))
    expectSelected('Dark theme')
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
    await user.click(screen.getByRole('button', { name: 'System theme' }))
    changeSystemTheme(false)
    expectSelected('System theme')
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')
  })

  it('falls back to the system preference for an unknown saved value', () => {
    localStorage.setItem('taskflow-theme', 'obsolete-setting')
    systemDark = true
    renderAppearance()

    expectSelected('System theme')
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
  })
})
