import type { GameFamily, Pokemon, TradeGame } from './types'

/** Como o jogo pede o código de troca (D21). */
export type LinkCodeKind = 'none' | 'digits8' | 'three_pokemon'

export interface GameDef {
  family: GameFamily
  /** Nome do jogo (nomes oficiais, iguais nos dois idiomas). */
  name: string
  versions: { id: TradeGame; name: string; color: string }[]
  extension: string
  linkCode: LinkCodeKind
  /** Modo LAN do jogo (sem placa), só no Sword/Shield (D21). */
  lan: boolean
  /** TID de 6 dígitos e SID de 4 (Gen 7 em diante). */
  sixDigitId: boolean
  trainerNameMax: number
  /** Espécie do ícone do cartão. */
  icon: number
}

// Mesma ordem e dados do GameCatalog do backend.
export const GAMES: GameDef[] = [
  {
    family: 'frlg',
    name: 'FireRed/LeafGreen',
    versions: [
      { id: 'fire_red', name: 'FireRed', color: '#d8443a' },
      { id: 'leaf_green', name: 'LeafGreen', color: '#3f9a4e' },
    ],
    extension: 'pk3',
    linkCode: 'none',
    lan: false,
    sixDigitId: false,
    trainerNameMax: 7,
    icon: 6,
  },
  {
    family: 'lgpe',
    name: "Let's Go, Pikachu!/Eevee!",
    versions: [
      { id: 'lets_go_pikachu', name: "Let's Go, Pikachu!", color: '#e8b923' },
      { id: 'lets_go_eevee', name: "Let's Go, Eevee!", color: '#a9743b' },
    ],
    extension: 'pb7',
    linkCode: 'three_pokemon',
    lan: false,
    sixDigitId: true,
    trainerNameMax: 12,
    icon: 25,
  },
  {
    family: 'swsh',
    name: 'Sword/Shield',
    versions: [
      { id: 'sword', name: 'Sword', color: '#2f8fd1' },
      { id: 'shield', name: 'Shield', color: '#c4335b' },
    ],
    extension: 'pk8',
    linkCode: 'digits8',
    lan: true,
    sixDigitId: true,
    trainerNameMax: 12,
    icon: 888,
  },
  {
    family: 'bdsp',
    name: 'Brilliant Diamond/Shining Pearl',
    versions: [
      { id: 'brilliant_diamond', name: 'Brilliant Diamond', color: '#4a7fd6' },
      { id: 'shining_pearl', name: 'Shining Pearl', color: '#d68ab0' },
    ],
    extension: 'pb8',
    linkCode: 'digits8',
    lan: false,
    sixDigitId: true,
    trainerNameMax: 12,
    icon: 483,
  },
  {
    family: 'pla',
    name: 'Legends: Arceus',
    versions: [{ id: 'legends_arceus', name: 'Legends: Arceus', color: '#b8a77a' }],
    extension: 'pa8',
    linkCode: 'digits8',
    lan: false,
    sixDigitId: true,
    trainerNameMax: 12,
    icon: 493,
  },
  {
    family: 'sv',
    name: 'Scarlet/Violet',
    versions: [
      { id: 'scarlet', name: 'Scarlet', color: '#e0472f' },
      { id: 'violet', name: 'Violet', color: '#7a4fc4' },
    ],
    extension: 'pk9',
    linkCode: 'digits8',
    lan: false,
    sixDigitId: true,
    trainerNameMax: 12,
    icon: 1007,
  },
  {
    family: 'za',
    name: 'Legends: Z-A',
    versions: [{ id: 'legends_za', name: 'Legends: Z-A', color: '#3fa86b' }],
    extension: 'pa9',
    linkCode: 'digits8',
    lan: false,
    sixDigitId: true,
    trainerNameMax: 12,
    icon: 716,
  },
]

export const gameOf = (version: TradeGame): GameDef => GAMES.find((g) => g.versions.some((v) => v.id === version))!

export const gameByFamily = (family: GameFamily): GameDef => GAMES.find((g) => g.family === family)!

export const versionName = (version: TradeGame) => gameOf(version).versions.find((v) => v.id === version)!.name

export const versionColor = (version: TradeGame) => gameOf(version).versions.find((v) => v.id === version)!.color

/** Pokémon antigos (antes da Entrega 14) não trazem o jogo: são do FireRed/LeafGreen. */
export const familyOf = (p: Pick<Pokemon, 'family'>): GameFamily => p.family ?? 'frlg'

/**
 * Pokémon da grade do código de troca do Let's Go, na ordem dos índices 0–9 (pokeldn/lgpe/session.py CODE_PICKER):
 * Pikachu, Eevee, Bulbasaur, Charmander, Squirtle, Pidgey, Caterpie, Rattata, Jigglypuff, Diglett.
 */
export const LGPE_CODE_SPECIES = [25, 133, 1, 4, 7, 16, 10, 19, 39, 50]

/** Limites de TID e SID como o jogo mostra. */
export const idLimits = (game: GameDef) => (game.sixDigitId ? { tid: 999999, sid: 4294 } : { tid: 65535, sid: 65535 })

/** Código de troca completo ou vazio (o jogo aceita os dois). Mesma regra do TradeRequest.IsValidLinkCode. */
export const linkCodeErrors = (game: TradeGame, code: string): string[] => {
  const kind = gameOf(game).linkCode
  if (code === '') return []
  if (kind === 'digits8') return /^\d{8}$/.test(code) ? [] : ['link_code_invalid']
  if (kind === 'three_pokemon') return /^\d,\d,\d$/.test(code) ? [] : ['link_code_invalid']
  return ['link_code_invalid']
}
