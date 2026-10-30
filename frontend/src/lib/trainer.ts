import type { GameDef } from './games'

// Mesma regra de TradeRock.Core.Trading.TrainerName (tabela Gen III do frlgsim/charmap.py).
const punctuation = ' .-·…¥×/“”‘’♂♀,!?'

export const TRAINER_NAME_MAX = 7

/**
 * Sem jogo (ou no FireRed/LeafGreen): até 7 caracteres da tabela Gen III, sem acentos. Nos jogos novos: até 12,
 * qualquer caractere visível.
 */
export function trainerNameErrors(name: string, game?: GameDef): string[] {
  if (!name) return ['trainer_name_empty']
  const errors: string[] = []
  const gen3 = !game || game.family === 'frlg'
  if ([...name].length > (game?.trainerNameMax ?? TRAINER_NAME_MAX)) errors.push('trainer_name_too_long')
  // eslint-disable-next-line no-control-regex
  const invalid = gen3 ? [...name].some((c) => !/^[A-Za-z0-9]$/.test(c) && !punctuation.includes(c)) : /[\u0000-\u001f\u007f]/.test(name)
  if (invalid) errors.push('trainer_name_invalid_chars')
  return errors
}

/** Treinador padrão do frlg-ldn-trade (DEFAULT_TRAINER). */
export const DEFAULT_TRAINER = { name: 'EMU', tid: 0x8822, sid: 0x47ed }
