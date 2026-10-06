import { describe, expect, it } from 'vitest'
import { connectionItems } from './connection'
import type { CheckStatus, SetupStatus } from './types'

const check = (id: string, step: number, status: CheckStatus, code = id) => ({ id, step, status, essential: true, code, args: null, fix: null, availableIn: null })

function setup(partial: { situation?: SetupStatus['advice']['situation']; radio?: 'wifi' | 'esp32' | null; checks: ReturnType<typeof check>[] }): SetupStatus {
  return {
    advice: { situation: partial.situation ?? 'wifi_compatible' },
    state: { radio: partial.radio ?? null },
    checks: partial.checks,
  } as unknown as SetupStatus
}

describe('lista de Configurações → Conexão', () => {
  it('sem rádio: chaves e rádio faltam, o resto espera', () => {
    const items = connectionItems(
      setup({ checks: [check('keys', 2, 'error', 'keys_missing'), check('radio', 3, 'error'), check('switch_test', 6, 'pending', 'switch_test_needs_radio')] }),
    )
    expect(items).toEqual({ system: 'ok', keys: 'missing', radio: 'missing', driver: 'blocked', switch: 'blocked' })
  })

  it('placa Wi-Fi: o pior item da etapa 4 vale para "componente de rede"; native_linux vira "sistema"', () => {
    const items = connectionItems(
      setup({
        radio: 'wifi',
        checks: [
          check('keys', 2, 'ok'),
          check('radio', 3, 'ok'),
          check('native_linux', 4, 'ok'),
          check('worker', 4, 'ok'),
          check('card_test', 4, 'error', 'card_test_not_run'),
          check('network_manager', 4, 'warning'),
          check('switch_test', 6, 'error', 'switch_test_not_run'),
        ],
      }),
    )
    expect(items).toEqual({ system: 'ok', keys: 'ok', radio: 'ok', driver: 'missing', switch: 'idle' })
  })

  it('WSL sem rádio escolhido: sistema pede atenção (só ESP32 funciona)', () => {
    expect(connectionItems(setup({ situation: 'wsl', checks: [] })).system).toBe('warn')
    expect(connectionItems(setup({ situation: 'wsl', radio: 'esp32', checks: [] })).system).toBe('ok')
  })

  it('sem dados ainda: tudo "não testado"', () => {
    expect(Object.values(connectionItems(null))).toEqual(['idle', 'idle', 'idle', 'idle', 'idle'])
  })
})
