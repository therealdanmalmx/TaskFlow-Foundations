import { createContext } from 'react'
import type { User } from '../model'

export interface AuthContextValue {
  user: User | null
  isLoading: boolean
  sessionError: string | null
  logoutError: string | null
  acceptSession: (user: User) => void
  updateUser: (user: User) => void
  retrySession: () => Promise<void>
  logout: () => Promise<void>
  clearLogoutError: () => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)
