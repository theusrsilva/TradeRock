import { describe, expect, it } from 'vitest'
import { circleReady, newCircle } from './CircleForm'
import { newRaid, raidReady } from './RaidForm'

describe('formulários das atividades', () => {
  it('raid só começa com o que o pokeldn exige', () => {
    expect(raidReady(newRaid('max-raid-host'))).toBe(false)
    expect(raidReady({ ...newRaid('max-raid-host'), boss: 'a', offer: 'b' })).toBe(true)
    expect(raidReady({ ...newRaid('max-raid-host'), event: 'Index 001', offer: 'b' })).toBe(false)
    expect(raidReady({ ...newRaid('max-raid-host'), event: 'Index 001', eventBoss: 'sword:5:1:Wild Area', offer: 'b' })).toBe(true)
    expect(raidReady({ ...newRaid('tera-raid-host'), pokemon: 'a' })).toBe(true)
    expect(raidReady({ ...newRaid('tera-raid-host'), pokemon: 'a', seed: 'XYZ' })).toBe(false)
    expect(raidReady({ ...newRaid('lair-host'), code: '123' })).toBe(false)
    expect(raidReady(newRaid('lair-join'))).toBe(true)
  })

  it('Union Circle precisa do código de 4 dígitos e do Pokémon nos encontros', () => {
    expect(circleReady(newCircle())).toBe(false)
    expect(circleReady({ ...newCircle(), code: '1234' })).toBe(true)
    expect(circleReady({ ...newCircle(), code: '1234', task: 'bp', bp: 0 })).toBe(false)
    expect(circleReady({ ...newCircle(), code: '1234', kind: 'encounters' })).toBe(false)
    expect(circleReady({ ...newCircle(), code: '1234', kind: 'encounters', species: 25 })).toBe(true)
  })
})
