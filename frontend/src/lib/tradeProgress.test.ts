import { describe, expect, it } from 'vitest'
import { instructionFor, receivedIds, timeline } from './tradeProgress'
import type { TradeEvent, TradeEventKind } from './types'

let seq = 0
function ev(kind: TradeEventKind, extra: Partial<TradeEvent> = {}): TradeEvent {
  return {
    kind,
    at: new Date().toISOString(),
    message: null,
    seq: ++seq,
    sessionId: 's',
    attempt: 1,
    tradeIndex: null,
    receivedPokemonId: null,
    errorCode: null,
    status: null,
    retryInSeconds: null,
    ...extra,
  }
}

describe('instrução da fase', () => {
  it('pede para aprovar o pedido de entrada', () => {
    expect(instructionFor([ev('session_started'), ev('scanning'), ev('join_requested')], 1).key).toBe('aprovar')
  })

  it('ignora log e erro ao decidir a fase', () => {
    expect(instructionFor([ev('join_requested'), ev('log')], 1).key).toBe('aprovar')
  })

  it('depois da última troca manda sair; antes, a próxima', () => {
    const one = [ev('trade_menu_open'), ev('trade_done', { tradeIndex: 0 })]
    expect(instructionFor(one, 2)).toEqual({ key: 'proxima', n: 1 })
    expect(instructionFor(one, 1).key).toBe('sair')
  })

  it('numera a troca atual', () => {
    const events = [ev('trade_done', { tradeIndex: 0 }), ev('trade_offered', { tradeIndex: 1 })]
    expect(instructionFor(events, 3)).toEqual({ key: 'escolher', n: 2 })
  })
})

describe('linha do tempo', () => {
  it('marca feitas, atual e pendentes', () => {
    const steps = timeline([ev('attempt_started'), ev('beacon_decrypted'), ev('join_requested')], 2, false)
    expect(steps.map((s) => s.state)).toEqual(['done', 'current', 'pending', 'pending', 'pending', 'pending', 'pending'])
    expect(steps.filter((s) => s.label === 'trade.steps.trade').map((s) => s.tradeNumber)).toEqual([1, 2])
  })

  it('uma nova tentativa recomeça a conexão', () => {
    const steps = timeline(
      [ev('attempt_started'), ev('beacon_decrypted'), ev('retry_scheduled'), ev('attempt_started')],
      1,
      false,
    )
    expect(steps[0].state).toBe('current')
  })

  it('sessão encerrada não tem etapa atual', () => {
    const steps = timeline([ev('attempt_started'), ev('error')], 1, true)
    expect(steps.some((s) => s.state === 'current' || s.state === 'failed')).toBe(false)
  })

  it('sessão que falhou marca onde parou', () => {
    const steps = timeline([ev('attempt_started'), ev('beacon_decrypted'), ev('join_requested'), ev('error')], 1, true, true)
    expect(steps.map((s) => s.state).slice(0, 3)).toEqual(['done', 'failed', 'pending'])
  })
})

it('coleta os recebidos por índice', () => {
  const ids = receivedIds([
    ev('trade_done', { tradeIndex: 1, receivedPokemonId: 'b' }),
    ev('trade_done', { tradeIndex: 0, receivedPokemonId: 'a' }),
  ])
  expect(ids).toEqual(['a', 'b'])
})

import { trainerNameErrors } from './trainer'

describe('nome do treinador', () => {
  it('aceita nomes da tabela Gen III', () => {
    expect(trainerNameErrors('Rocha')).toEqual([])
    expect(trainerNameErrors('RED♂')).toEqual([])
  })
  it('recusa acentos e nomes longos', () => {
    expect(trainerNameErrors('João')).toContain('trainer_name_invalid_chars')
    expect(trainerNameErrors('Matheus8')).toContain('trainer_name_too_long')
    expect(trainerNameErrors('')).toEqual(['trainer_name_empty'])
  })
})
