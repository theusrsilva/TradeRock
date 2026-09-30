import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from './api'
import type { TradeEvent, TradeSession } from './types'

/**
 * Estado da troca atual: snapshot do backend + eventos ao vivo por SSE.
 * O EventSource reconecta sozinho (mandando Last-Event-ID); o backend reenvia o que faltou.
 */
export function useTradeSession() {
  const [session, setSession] = useState<TradeSession | null>(null)
  const [events, setEvents] = useState<TradeEvent[]>([])
  const [connected, setConnected] = useState(false)
  const sessionIdRef = useRef<string | null>(null)

  const refresh = useCallback(async () => {
    const current = await api.get<TradeSession | undefined>('/api/trades/current')
    setSession(current ?? null)
    return current ?? null
  }, [])

  useEffect(() => {
    void refresh()
    const source = new EventSource('/api/trades/events')
    source.onopen = () => setConnected(true)
    source.onerror = () => setConnected(false)
    source.addEventListener('trade', (message) => {
      const event = JSON.parse((message as MessageEvent<string>).data) as TradeEvent
      if (event.sessionId !== sessionIdRef.current) {
        // Nova sessão: começa uma lista nova.
        sessionIdRef.current = event.sessionId
        setEvents([event])
      } else {
        setEvents((list) => (list.some((e) => e.seq === event.seq) ? list : [...list, event]))
      }
      if (event.kind === 'session_started' || event.kind === 'session_finished' || event.kind === 'attempt_started'
        || event.kind === 'trade_done') {
        void refresh()
      }
    })
    return () => source.close()
  }, [refresh])

  const sessionEvents = session ? events.filter((e) => e.sessionId === session.id) : []
  return { session, events: sessionEvents, connected, refresh }
}
