// Mesma regra de TradeRock.Core.Trading.TrainerName (tabela Gen III do frlgsim/charmap.py).
const punctuation = ' .-·…¥×/“”‘’♂♀,!?'

export const TRAINER_NAME_MAX = 7

export function trainerNameErrors(name: string): string[] {
  if (!name) return ['trainer_name_empty']
  const errors: string[] = []
  if ([...name].length > TRAINER_NAME_MAX) errors.push('trainer_name_too_long')
  if ([...name].some((c) => !/^[A-Za-z0-9]$/.test(c) && !punctuation.includes(c))) errors.push('trainer_name_invalid_chars')
  return errors
}

/** Treinador padrão do frlg-ldn-trade (DEFAULT_TRAINER). */
export const DEFAULT_TRAINER = { name: 'EMU', tid: 0x8822, sid: 0x47ed }
