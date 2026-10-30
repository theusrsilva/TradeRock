import { describe, expect, it } from 'vitest'
import catalog from '../../../catalog/gifts.json'
import pt from '../locales/gifts.pt-BR.json'

// catalog/gifts.json vem do pokeldn (scripts/pokeldn/gift_catalog.py); cada presente pronto e cada grupo precisa
// de texto em português.
describe('presentes do Mystery Gift em português', () => {
  const games = catalog as Record<string, { presets: { key: string; group: string; when: string }[]; events: { group: string }[] }>

  it('todo presente pronto tem nome e descrição', () => {
    for (const [game, gifts] of Object.entries(games)) {
      const texts = (pt.presets as Record<string, Record<string, string[]>>)[game] ?? {}
      for (const p of gifts.presets) expect(texts[p.key], `${game}/${p.key}`).toHaveLength(2)
      expect(Object.keys(texts).sort()).toEqual(gifts.presets.map((p) => p.key).sort())
    }
  })

  it('todo grupo e todo aviso têm tradução', () => {
    for (const gifts of Object.values(games)) {
      for (const g of [...gifts.presets, ...gifts.events]) expect(pt.groups, g.group).toHaveProperty([g.group])
      for (const p of gifts.presets) if (p.when) expect(pt.when, p.when).toHaveProperty([p.when])
    }
  })
})
