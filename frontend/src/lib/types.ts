// Tipos da API (enums em snake_case, como o backend serializa).

export type TradeGame = 'fire_red' | 'leaf_green'
export type PokemonOrigin = 'imported' | 'received' | 'demo'
export type TradeSessionStatus = 'running' | 'completed' | 'partially_completed' | 'failed' | 'cancelled'
export type SimulationScenario =
  | 'success'
  | 'connect_failures_then_success'
  | 'no_network_found'
  | 'wrong_keys'
  | 'approval_never_given'
  | 'join_rejected'
  | 'host_cancels'
  | 'connection_lost_mid_trade'

export type TradeEventKind =
  | 'session_started'
  | 'attempt_started'
  | 'retry_scheduled'
  | 'session_finished'
  | 'scanning'
  | 'beacon_found'
  | 'beacon_decrypted'
  | 'join_requested'
  | 'joined'
  | 'in_room'
  | 'host_sat'
  | 'cards_exchanged'
  | 'trade_menu_open'
  | 'trade_offered'
  | 'confirming'
  | 'trade_done'
  | 'trade_cancelled'
  | 'leaving'
  | 'link_closed'
  | 'error'
  | 'log'

export interface Health {
  app: string
  version: string
  os: 'linux' | 'windows' | 'other'
  environment: string
  window: boolean
}

export interface Pokemon {
  id: string
  species: number
  speciesName: string
  nickname: string
  level: number
  isShiny: boolean
  isEgg: boolean
  gender: number
  otName: string
  tid: number
  sid: number
  originGame: string
  origin: PokemonOrigin
  sourceFileName: string | null
  size: number
  sha256: string
  addedAt: string
  heldItem: number
  evolvesOnTradeInto: number | null
}

export interface UploadResult {
  fileName: string
  ok: boolean
  created: boolean
  pokemon: Pokemon | null
  error: string | null
  errorMessage: string | null
}

export interface TradeEvent {
  kind: TradeEventKind
  at: string
  message: string | null
  seq: number
  sessionId: string
  attempt: number | null
  tradeIndex: number | null
  receivedPokemonId: string | null
  errorCode: string | null
  status: TradeSessionStatus | null
  retryInSeconds: number | null
}

export interface TradeSession {
  id: string
  engineId: string
  isSimulation: boolean
  status: TradeSessionStatus
  attempt: number
  maxAttempts: number
  tradesPlanned: number
  tradesCompleted: number
  startedAt: string
  endedAt: string | null
  trainerName: string
  game: TradeGame
  failureCode: string | null
  failureMessage: string | null
  receivedPokemonIds: (string | null)[]
}

export interface TradeHistoryItem {
  id: string
  startedAt: string
  endedAt: string | null
  engineId: string
  isSimulation: boolean
  game: TradeGame
  trainerName: string
  tid: number
  sid: number | null
  status: TradeSessionStatus
  attempts: number
  tradesPlanned: number
  tradesCompleted: number
  failureCode: string | null
  failureMessage: string | null
  entries: { tradeIndex: number; sentPokemonId: string; receivedPokemonId: string | null }[]
}

export interface TradeSettings {
  maxAttempts: number
  retryDelaySeconds: number
  connectTimeoutSeconds: number
  approvalTimeoutSeconds: number
  phaseTimeoutSeconds: number
  totalTimeoutMinutes: number
}

export interface Engine {
  id: string
  isSimulation: boolean
  available: boolean
  reasonCode: string | null
  selected: boolean
}

export interface Trainer {
  name: string
  tid: number
  sid: number | null
}

export interface TrainerPreset extends Trainer {
  id: string
  label: string
  game: TradeGame
}

export interface StartTrade {
  party: string[]
  offeredSlots: number[]
  game: TradeGame
  trainer: Trainer
  simulation: { scenario: SimulationScenario; failuresBeforeSuccess: number } | null
}
