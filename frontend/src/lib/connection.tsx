import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { api } from './api'
import { useIsRemote } from './auth'
import type { CheckStatus, Engine, SetupCheck, SetupStatus } from './types'

/** Os cinco itens da conexão mostrados no Início, no menu e na lista de Configurações → Conexão. */
export const connectionKeys = ['system', 'keys', 'radio', 'driver', 'switch'] as const
export type ConnectionKey = (typeof connectionKeys)[number]

/** Os itens obrigatórios para a troca; o teste com o Switch é recomendado, não obrigatório. */
export const requiredKeys: readonly ConnectionKey[] = ['system', 'keys', 'radio', 'driver']

/** Situação de um item: ok, atenção, falta, esperando outro item ou ainda não testado. */
export type ItemState = 'ok' | 'warn' | 'missing' | 'blocked' | 'idle'

export interface ConnectionState {
  setup: SetupStatus | null
  engines: Engine[]
  /** Motor escolhido é a simulação: as trocas são de mentira. */
  demo: boolean
  items: Record<ConnectionKey, ItemState>
  /** Itens obrigatórios que já passaram (de requiredKeys.length). */
  readyCount: number
  ready: boolean
  reload: () => Promise<SetupStatus | null>
  setSetup: (status: SetupStatus) => void
  reloadEngines: () => Promise<void>
}

const rank: Record<CheckStatus, number> = { ok: 0, info: 0, warning: 1, pending: 2, error: 3 }

function worst(checks: SetupCheck[]): CheckStatus | null {
  if (checks.length === 0) return null
  return checks.reduce((a, c) => (rank[c.status] > rank[a.status] ? c : a)).status
}

function toState(status: CheckStatus | null, whenEmpty: ItemState): ItemState {
  switch (status) {
    case null:
      return whenEmpty
    case 'ok':
    case 'info':
      return 'ok'
    case 'warning':
      return 'warn'
    case 'pending':
      return 'blocked'
    default:
      return 'missing'
  }
}

/** Ids das verificações da etapa 4 que contam como "Sistema" (o resto conta como componente de rede/USB). */
export const systemCheckIds = ['native_linux']

/** Agrupa as verificações reais do backend nos cinco itens da lista. */
export function connectionItems(setup: SetupStatus | null): Record<ConnectionKey, ItemState> {
  if (!setup) return { system: 'idle', keys: 'idle', radio: 'idle', driver: 'idle', switch: 'idle' }
  const by = (id: string) => setup.checks.filter((c) => c.id === id)
  const sys = by('native_linux')
  // Sem placa Wi-Fi possível (Windows, WSL, máquina virtual) o sistema só serve com ESP32.
  const noWifi = ['windows', 'wsl', 'vm'].includes(setup.advice.situation)
  const systemState: ItemState =
    sys.length > 0 ? toState(worst(sys), 'ok') : setup.state.radio === 'esp32' ? 'ok' : noWifi ? 'warn' : 'ok'
  const step4 = setup.checks.filter((c) => c.step === 4 && !systemCheckIds.includes(c.id))
  const switchCheck = by('switch_test')[0]
  return {
    system: systemState,
    keys: toState(worst(by('keys')), 'missing'),
    radio: toState(worst(by('radio')), 'missing'),
    driver: setup.state.radio ? toState(worst(step4), 'ok') : 'blocked',
    switch: switchCheck?.code === 'switch_test_not_run' ? 'idle' : toState(switchCheck?.status ?? null, 'blocked'),
  }
}

const ConnectionContext = createContext<ConnectionState | null>(null)

export function ConnectionProvider({ children }: { children: ReactNode }) {
  const remote = useIsRemote()
  const [setup, setSetup] = useState<SetupStatus | null>(null)
  const [engines, setEngines] = useState<Engine[]>([])

  const reload = useCallback(async () => {
    // A configuração da conexão é só da janela do PC.
    if (remote) return null
    try {
      const s = await api.get<SetupStatus>('/api/setup')
      setSetup(s)
      return s
    } catch {
      return null
    }
  }, [remote])

  const reloadEngines = useCallback(async () => {
    try {
      setEngines(await api.get<Engine[]>('/api/engines'))
    } catch {
      // ignora
    }
  }, [])

  useEffect(() => {
    void reload()
    void reloadEngines()
  }, [reload, reloadEngines])

  const items = connectionItems(setup)
  const readyCount = requiredKeys.filter((k) => items[k] === 'ok').length
  const selected = engines.find((e) => e.selected)
  const demo = selected?.isSimulation ?? false
  // Pela rede não dá para ver as verificações: "pronto" é ter um motor de verdade escolhido. O modo LAN não precisa
  // da configuração da conexão (D24).
  const ready = !!selected && !demo && (remote || !selected.needsSetup || (setup?.readyToTrade ?? false))

  return (
    <ConnectionContext.Provider value={{ setup, engines, demo, items, readyCount, ready, reload, setSetup, reloadEngines }}>
      {children}
    </ConnectionContext.Provider>
  )
}

export function useConnection(): ConnectionState {
  const ctx = useContext(ConnectionContext)
  if (!ctx) throw new Error('ConnectionProvider ausente')
  return ctx
}
