import i18n from 'i18next'
import pt from '../locales/gifts.pt-BR.json'
import type { GameFamily, GiftEntry } from './types'

/**
 * Textos dos presentes do Mystery Gift (D26). O catálogo (catalog/gifts.json) vem do pokeldn, em inglês. Os nomes
 * (presentes prontos, eventos e grupos) ficam como no jogo e no pokeldn, em inglês, a pedido do autor; em português,
 * só as descrições e os avisos têm tradução, em locales/gifts.pt-BR.json.
 */
const portuguese = () => i18n.language.startsWith('pt')

export function giftText(family: GameFamily, gift: GiftEntry): { label: string; summary: string } {
  const summaries = (pt.presets as Record<string, Record<string, string>>)[family]
  const summary = portuguese() ? summaries?.[gift.key] : undefined
  return { label: gift.label, summary: summary ?? gift.summary }
}

export function whenText(when: string): string {
  return (portuguese() && (pt.when as Record<string, string>)[when]) || when
}
