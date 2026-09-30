import type { TradeEvent, TradeEventKind } from './types'

/** Chave da instrução em content/<idioma>/trade/fases.md para o momento atual. */
export type InstructionKey =
  | 'procurando'
  | 'tentando_de_novo'
  | 'aprovar'
  | 'andar_cadeira'
  | 'aguarde'
  | 'escolher'
  | 'confirmar'
  | 'proxima'
  | 'sair'
  | 'cancelado_switch'
  | 'fim'

/** Último evento que indica progresso (ignora log e erro). */
export function lastProgress(events: TradeEvent[]): TradeEvent | undefined {
  for (let i = events.length - 1; i >= 0; i--) {
    const kind = events[i].kind
    if (kind !== 'log' && kind !== 'error') return events[i]
  }
  return undefined
}

export function instructionFor(events: TradeEvent[], totalTrades: number): { key: InstructionKey; n: number } {
  const last = lastProgress(events)
  const done = events.filter((e) => e.kind === 'trade_done').length
  const current = Math.min(done + 1, totalTrades)
  if (!last) return { key: 'procurando', n: current }

  const byKind: Partial<Record<TradeEventKind, InstructionKey>> = {
    session_started: 'procurando',
    attempt_started: 'procurando',
    scanning: 'procurando',
    beacon_found: 'procurando',
    beacon_decrypted: 'procurando',
    retry_scheduled: 'tentando_de_novo',
    join_requested: 'aprovar',
    joined: 'andar_cadeira',
    in_room: 'andar_cadeira',
    host_sat: 'aguarde',
    cards_exchanged: 'aguarde',
    trade_menu_open: 'escolher',
    trade_offered: 'escolher',
    confirming: 'confirmar',
    trade_cancelled: 'cancelado_switch',
    leaving: 'sair',
    link_closed: 'fim',
    session_finished: 'fim',
  }

  if (last.kind === 'trade_done') {
    return done >= totalTrades ? { key: 'sair', n: done } : { key: 'proxima', n: done }
  }
  return { key: byKind[last.kind] ?? 'aguarde', n: current }
}

export type StepState = 'done' | 'current' | 'failed' | 'pending'

export interface Step {
  id: string
  /** Chave i18n do rótulo. */
  label: string
  tradeNumber?: number
  state: StepState
}

// Ordem das etapas visíveis e o evento que conclui cada uma.
const baseSteps: { id: string; label: string; doneWhen: TradeEventKind }[] = [
  { id: 'search', label: 'trade.steps.search', doneWhen: 'beacon_decrypted' },
  { id: 'approve', label: 'trade.steps.approve', doneWhen: 'joined' },
  { id: 'sit', label: 'trade.steps.sit', doneWhen: 'host_sat' },
  { id: 'menu', label: 'trade.steps.menu', doneWhen: 'trade_menu_open' },
]

/**
 * Etapas da troca. Com a sessão encerrada sem sucesso (<paramref>failed</paramref>), a primeira
 * etapa não concluída fica marcada como a que falhou.
 */
export function timeline(events: TradeEvent[], totalTrades: number, finished: boolean, failed = false): Step[] {
  // Numa nova tentativa, o progresso de conexão recomeça: só contam eventos da última tentativa.
  const lastAttemptStart = events.map((e) => e.kind).lastIndexOf('attempt_started')
  const attemptEvents = lastAttemptStart >= 0 ? events.slice(lastAttemptStart) : events
  const seen = new Set(attemptEvents.map((e) => e.kind))
  const tradesDone = events.filter((e) => e.kind === 'trade_done').length

  const steps: Omit<Step, 'state'>[] = [
    ...baseSteps.map(({ id, label }) => ({ id, label })),
    ...Array.from({ length: totalTrades }, (_, i) => ({ id: `trade-${i}`, label: 'trade.steps.trade', tradeNumber: i + 1 })),
    { id: 'leave', label: 'trade.steps.leave' },
  ]

  const isDone = (index: number): boolean => {
    if (index < baseSteps.length) return seen.has(baseSteps[index].doneWhen)
    const tradeIndex = index - baseSteps.length
    if (tradeIndex < totalTrades) return tradeIndex < tradesDone
    return seen.has('link_closed')
  }

  let marked = finished && !failed
  return steps.map((step, index) => {
    if (isDone(index)) return { ...step, state: 'done' }
    if (!marked) {
      marked = true
      return { ...step, state: finished ? 'failed' : 'current' }
    }
    return { ...step, state: 'pending' }
  })
}

/** Ids recebidos por índice de troca. */
export function receivedIds(events: TradeEvent[]): (string | null)[] {
  const result: (string | null)[] = []
  for (const e of events) {
    if (e.kind === 'trade_done' && e.tradeIndex !== null && e.receivedPokemonId) {
      result[e.tradeIndex] = e.receivedPokemonId
    }
  }
  return result
}
