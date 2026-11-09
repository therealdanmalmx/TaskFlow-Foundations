import { createContext } from 'react'

export type Theme = 'light' | 'dark' | 'system'
export const ThemeContext = createContext<{ theme: Theme; setTheme: (theme: Theme) => void } | null>(null)
