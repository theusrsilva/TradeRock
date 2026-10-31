import i18n from 'i18next'
import pt from '../locales/gifts.pt-BR.json'
import type { GameFamily, GiftEntry } from './types'

/**
 * Textos dos presentes do Mystery Gift (D26). O catálogo (catalog/gifts.json) vem do pokeldn, em inglês; em
 * português, os presentes prontos e os grupos têm tradução em locales/gifts.pt-BR.json. Os eventos oficiais
 * ficam com o nome original.
 */
const portuguese = () => i18n.language.startsWith('pt')

export function giftText(family: GameFamily, gift: GiftEntry): { label: string; summary: string } {
  const presets = (pt.presets as Record<string, Record<string, string[]>>)[family]
  const text = portuguese() ? presets?.[gift.key] : undefined
  return text ? { label: text[0], summary: text[1] } : { label: gift.label, summary: gift.summary }
}

export function groupName(group: string): string {
  return (portuguese() && (pt.groups as Record<string, string>)[group]) || group
}

export function whenText(when: string): string {
  return (portuguese() && (pt.when as Record<string, string>)[when]) || when
}
