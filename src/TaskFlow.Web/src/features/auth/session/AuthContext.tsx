import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { ApiError, unauthorizedEvent } from '../../../shared/api/HttpClient'
import { getCurrentUser, logout as logoutRequest } from './api'
import { AuthContext, type AuthContextValue } from './context'
import type { User } from '../model'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [sessionError, setSessionError] = useState<string | null>(null)
  const [logoutError, setLogoutError] = useState<string | null>(null)
  const requestGenerationRef = useRef(0)
  const requestControllerRef = useRef<AbortController | null>(null)

  const retrySession = useCallback(async () => {
    const generation = ++requestGenerationRef.current
    requestControllerRef.current?.abort()
    const controller = new AbortController()
    requestControllerRef.current = controller
    setIsLoading(true)
    setSessionError(null)
    try {
      const currentUser = await getCurrentUser(controller.signal)
      if (generation !== requestGenerationRef.current) return
      setUser(currentUser)
    } catch (error) {
      if (controller.signal.aborted || generation !== requestGenerationRef.current) return
      setUser(null)
      if (!(error instanceof ApiError && error.status === 401)) {
        setSessionError('TaskFlow could not verify your session. The API may be restarting.')
      }
    } finally {
      if (generation === requestGenerationRef.current) setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    const endSession = () => setUser(null)
    window.addEventListener(unauthorizedEvent, endSession)
    void retrySession()
    return () => {
      requestGenerationRef.current += 1
      requestControllerRef.current?.abort()
      window.removeEventListener(unauthorizedEvent, endSession)
    }
  }, [retrySession])

  const acceptSession = useCallback((acceptedUser: User) => {
    requestGenerationRef.current += 1
    requestControllerRef.current?.abort()
    setUser(acceptedUser)
    setIsLoading(false)
    setSessionError(null)
    setLogoutError(null)
  }, [])

  const logout = useCallback(async () => {
    const generation = ++requestGenerationRef.current
    requestControllerRef.current?.abort()
    setLogoutError(null)
    try {
      await logoutRequest()
      if (generation !== requestGenerationRef.current) return
      setUser(null)
    } catch (requestError) {
      if (generation !== requestGenerationRef.current) throw requestError

      const controller = new AbortController()
      requestControllerRef.current = controller
      try {
        const currentUser = await getCurrentUser(controller.signal)
        if (generation === requestGenerationRef.current) {
          setUser(currentUser)
          setLogoutError('TaskFlow could not confirm sign out. You are still signed in; please try again.')
        }
      } catch (reconcileError) {
        if (controller.signal.aborted || generation !== requestGenerationRef.current) throw requestError
        if (reconcileError instanceof ApiError && reconcileError.status === 401) {
          setUser(null)
        } else {
          setLogoutError('TaskFlow could not confirm sign out because the API is unavailable. Your session may still be active.')
        }
      }
      throw requestError
    }
  }, [])

  const value = useMemo<AuthContextValue>(() => ({
    user,
    isLoading,
    sessionError,
    logoutError,
    acceptSession,
    updateUser: setUser,
    retrySession,
    logout,
    clearLogoutError: () => setLogoutError(null),
  }), [acceptSession, isLoading, logout, logoutError, retrySession, sessionError, user])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
