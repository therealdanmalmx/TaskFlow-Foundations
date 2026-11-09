import { useEffect, useState } from 'react'

export type BoardDensity = 'compact' | 'detailed'

const storageKey = 'taskflow-board-density'

function readDensity(): BoardDensity {
  try {
    return localStorage.getItem(storageKey) === 'compact' ? 'compact' : 'detailed'
  } catch {
    return 'detailed'
  }
}

export function useBoardDensity() {
  const [density, updateDensity] = useState<BoardDensity>(readDensity)

  useEffect(() => {
    const sync = (event: StorageEvent) => {
      if (event.key === storageKey || event.key === null) updateDensity(readDensity())
    }
    window.addEventListener('storage', sync)
    return () => window.removeEventListener('storage', sync)
  }, [])

  function setDensity(next: BoardDensity) {
    updateDensity(next)
    try { localStorage.setItem(storageKey, next) } catch { /* The preference still works for this session. */ }
  }

  return { density, setDensity }
}
