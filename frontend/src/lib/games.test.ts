import { describe, expect, it } from 'vitest'
import { GAMES, gameOf, idLimits, linkCodeErrors, LGPE_CODE_SPECIES } from './games'
import { trainerNameErrors } from './trainer'
import type { TradeGame } from './types'

describe('jogos', () => {
  it('cada versão pertence a um jogo, sem repetir', () => {
    const versions = GAMES.flatMap((g) => g.versions.map((v) => v.id))
    expect(new Set(versions).size).toBe(versions.length)
    expect(versions).toHaveLength(12)
    expect(gameOf('violet').family).toBe('sv')
    expect(gameOf('legends_za').extension).toBe('pa9')
  })

  it('só o Sword/Shield tem modo LAN (D21)', () => {
    expect(GAMES.filter((g) => g.lan).map((g) => g.family)).toEqual(['swsh'])
  })

  it('IDs de 6 dígitos da Gen 7 em diante', () => {
    expect(idLimits(gameOf('fire_red'))).toEqual({ tid: 65535, sid: 65535 })
    expect(idLimits(gameOf('scarlet'))).toEqual({ tid: 999999, sid: 4294 })
  })

  it('nome do treinador: 7 da tabela Gen III no FRLG, 12 livres nos outros', () => {
    expect(trainerNameErrors('João')).toContain('trainer_name_invalid_chars')
    expect(trainerNameErrors('João', gameOf('scarlet'))).toEqual([])
    expect(trainerNameErrors('Treinadora', gameOf('fire_red'))).toContain('trainer_name_too_long')
    expect(trainerNameErrors('Treinadora', gameOf('sword'))).toEqual([])
    expect(trainerNameErrors('Treinadora Ana', gameOf('sword'))).toContain('trainer_name_too_long')
  })

  it('código de troca', () => {
    const cases: [TradeGame, string, boolean][] = [
      ['scarlet', '', true],
      ['scarlet', '12345678', true],
      ['scarlet', '1234', false],
      ['lets_go_eevee', '0,4,9', true],
      ['lets_go_eevee', '0,4', false],
      ['fire_red', '12345678', false],
    ]
    for (const [game, code, ok] of cases) expect(linkCodeErrors(game, code).length === 0).toBe(ok)
    expect(LGPE_CODE_SPECIES).toHaveLength(10)
  })
})
