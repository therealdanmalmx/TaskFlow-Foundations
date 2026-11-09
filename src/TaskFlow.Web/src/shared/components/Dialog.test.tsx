import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { Dialog } from './Dialog'

describe('Dialog', () => {
  it('keeps forward and reverse Tab navigation inside the modal', async () => {
    render(
      <>
        <button type="button">Outside</button>
        <Dialog open title="Edit details" onClose={vi.fn()}>
          <label>Name <input aria-label="Name" /></label>
          <button type="button">Save</button>
        </Dialog>
      </>,
    )

    const close = screen.getByRole('button', { name: 'Close Edit details' })
    const save = screen.getByRole('button', { name: 'Save' })
    await waitFor(() => expect(close).toHaveFocus())

    save.focus()
    fireEvent.keyDown(document, { key: 'Tab' })
    expect(close).toHaveFocus()

    close.focus()
    fireEvent.keyDown(document, { key: 'Tab', shiftKey: true })
    expect(save).toHaveFocus()

    screen.getByRole('button', { name: 'Outside' }).focus()
    fireEvent.keyDown(document, { key: 'Tab' })
    expect(close).toHaveFocus()
  })

  it('blocks every dismissal path while an operation is pending', () => {
    const onClose = vi.fn()
    const view = render(
      <Dialog open title="Delete task?" onClose={onClose} closeDisabled>
        <button type="button">Working…</button>
      </Dialog>,
    )

    const dialog = screen.getByRole('dialog', { name: 'Delete task?' })
    const close = screen.getByRole('button', { name: 'Close Delete task?' })
    expect(close).toBeDisabled()

    fireEvent.keyDown(document, { key: 'Escape' })
    fireEvent.mouseDown(dialog.parentElement!)
    fireEvent.click(close)
    expect(onClose).not.toHaveBeenCalled()

    view.rerender(
      <Dialog open title="Delete task?" onClose={onClose} closeDisabled={false}>
        <button type="button">Try again</button>
      </Dialog>,
    )
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
