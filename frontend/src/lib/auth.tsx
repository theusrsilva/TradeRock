import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, setCsrfToken, unauthorizedEvent } from './api'
import type { Me } from './types'

interface AuthState {
  me: Me | null
  reload: () => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthState>({ me: null, reload: async () => undefined, logout: async () => undefined })

/**
 * Quem está usando: a janela local (dono do PC) ou uma conta que entrou pela rede (modo servidor).
 * Pela rede, sem login, a tela de entrada aparece no lugar do app.
 */
export function AuthProvider({ children, fallback }: { children: ReactNode; fallback: (reload: () => Promise<void>) => ReactNode }) {
  const [me, setMe] = useState<Me | null>(null)
  const [loaded, setLoaded] = useState(false)

  const reload = useCallback(async () => {
    try {
      const result = await api.get<Me>('/api/auth/me')
      setCsrfToken(result.csrf)
      setMe(result)
    } catch {
      setMe(null)
    } finally {
      setLoaded(true)
    }
  }, [])

  const logout = useCallback(async () => {
    try {
      await api.post('/api/auth/logout')
    } finally {
      setCsrfToken(null)
      await reload()
    }
  }, [reload])

  useEffect(() => {
    void reload()
    const onUnauthorized = () => void reload()
    window.addEventListener(unauthorizedEvent, onUnauthorized)
    return () => window.removeEventListener(unauthorizedEvent, onUnauthorized)
  }, [reload])

  if (!loaded) return null
  if (me && !me.authenticated) return <>{fallback(reload)}</>
  return <AuthContext.Provider value={{ me, reload, logout }}>{children}</AuthContext.Provider>
}

export function useAuth() {
  return useContext(AuthContext)
}

/** Pela rede (não é a janela local do PC). */
export function useIsRemote() {
  return useAuth().me?.local === false
}
