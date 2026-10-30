// Tipos da API (enums em snake_case, como o backend serializa).

export type TradeGame =
  | 'fire_red'
  | 'leaf_green'
  | 'lets_go_pikachu'
  | 'lets_go_eevee'
  | 'sword'
  | 'shield'
  | 'brilliant_diamond'
  | 'shining_pearl'
  | 'legends_arceus'
  | 'scarlet'
  | 'violet'
  | 'legends_za'
/** Jogo do Switch (as duas versões de um par juntas), como o backend agrupa (GameCatalog). */
export type GameFamily = 'frlg' | 'lgpe' | 'swsh' | 'bdsp' | 'pla' | 'sv' | 'za'
export type PokemonOrigin = 'imported' | 'received' | 'demo' | 'created'
export type LegalityStatus = 'legal' | 'illegal' | 'unverifiable'
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
  family: GameFamily
  species: number
  form: number
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
  fateful: boolean
  legality: LegalityStatus | null
  warnings: TradeWarning[]
}

export interface TradeWarning {
  code: 'mythical_not_fateful' | 'national_dex_unknown' | 'national_dex_required' | 'trade_evolution' | 'obedience'
  args: Record<string, number | string>
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
  ownerId: string
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
  allowIllegal: boolean
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
  acceptIllegal: boolean
  /** Oito dígitos, ou no Let's Go três números da grade ("0,4,9"). Vazio = sem código. */
  linkCode?: string | null
}

// ---- Construtor ----

export interface Option {
  value: number
  text: string
}

export interface BuilderData {
  species: Option[]
  natures: Option[]
  moves: Option[]
  items: Option[]
  balls: Option[]
  languages: Option[]
  versions: string[]
  maxEv: number
  nicknameLength: number
  /** Modo avançado (só FireRed/LeafGreen). */
  advanced: boolean
}

export interface SpeciesDetails {
  species: number
  name: string
  abilities: Option[]
  genderRatio: 'genderless' | 'male_only' | 'female_only' | 'mixed'
  inKantoDex: boolean
  /** Formas que existem no jogo (Value = número da forma); null no FRLG. */
  forms: Option[] | null
}

export interface EncounterOption {
  key: string
  type: 'wild' | 'static' | 'gift' | 'event' | 'trade' | 'egg' | 'shadow' | 'other'
  version: string
  species: number
  speciesName: string
  location: number
  locationName: string
  levelMin: number
  levelMax: number
  fateful: boolean
}

export interface TrainerSpec {
  name: string
  tid: number
  sid: number
  gender: number
}

export interface PokemonSpec {
  species: number
  form?: number
  nickname: string | null
  level: number
  nature: number | null
  abilitySlot: number | null
  gender: 'M' | 'F' | null
  shiny: boolean
  ivs: number[] | null
  evs: number[]
  moves: number[]
  heldItem: number
  ball: number | null
  language: number
  game: TradeGame
  encounterKey: string | null
  trainer: TrainerSpec
}

export interface RawPokemon {
  species: number
  nickname: string | null
  level: number
  pid: number
  abilityBit: boolean
  ivs: number[]
  evs: number[]
  moves: number[]
  ppUps: number[]
  heldItem: number
  ball: number
  otName: string
  otGender: number
  tid: number
  sid: number
  version: string
  language: number
  metLocation: number
  metLevel: number
  fateful: boolean
  friendship: number
}

export interface LegalityCheck {
  identifier: string
  severity: 'valid' | 'fishy' | 'invalid'
  code: string
  message: string
  field: string | null
  moveIndex: number | null
}

export interface LegalityReport {
  status: LegalityStatus
  checks: LegalityCheck[]
  encounter: string | null
  encounterLocation: string | null
  unverifiableReason: string | null
  pkhexVersion: string
}

export interface Pk3Summary {
  family?: GameFamily
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
  language: number
  heldItem: number
  fateful: boolean
}

export interface SpecDifference {
  field: string
  requested: string
  obtained: string
}

export interface BuildResult {
  generation: 'regenerated' | 'failed' | 'timeout' | 'versionmismatch' | 'invalid_request'
  errors: string[]
  data: string | null
  summary: Pk3Summary | null
  obtained: PokemonSpec | null
  legality: LegalityReport | null
  differences: SpecDifference[]
  encounter: EncounterOption | null
  raw: RawPokemon | null
  usable: boolean
  blockers: { code: string; args: Record<string, number | string> }[] | null
}

export interface AdvancedResult {
  errors: string[]
  data: string | null
  summary: Pk3Summary | null
  legality: LegalityReport | null
  derived: { nature: number; gender: 'M' | 'F' | null; shiny: boolean; ability: string; pidIvMethod: string } | null
  raw: RawPokemon | null
}

export interface MyTrainer {
  name: string
  tid: number
  sid: number | null
  gender: number
  badges: number
  hasNationalDex: boolean
}

export interface ShowdownImportItem {
  spec: PokemonSpec | null
  problems: string[]
  text: string
}

export interface SaveUpload {
  token: string
  game: string
  trainerName: string
  tid: number
  sid: number
  entries: { index: number; place: 'party' | 'box'; box: number; slot: number; summary: Pk3Summary; legality: LegalityStatus }[]
}

// ---- Cofre ----

export interface VaultPokemon extends Pokemon {
  boxId: string | null
  slot: number | null
  deletedAt: string | null
  tagIds: string[]
  possibleDuplicate: boolean
}

export interface BoxSummary {
  id: string
  name: string
  order: number
  count: number
}

export interface VaultOverview {
  boxes: BoxSummary[]
  inboxCount: number
  trashCount: number
}

export interface Tag {
  id: string
  name: string
  color: TagColor
}

export type TagColor = 'slate' | 'red' | 'amber' | 'emerald' | 'sky' | 'violet' | 'pink'

export interface SaveLayoutInfo {
  token: string
  fileName: string
  inPlace: boolean
  layout: {
    game: string
    trainerName: string
    tid: number
    sid: number
    boxCount: number
    slotsPerBox: number
    boxNames: string[]
    occupied: { box: number; slot: number; species: number; speciesName: string; nickname: string; level: number; isEgg: boolean }[]
  }
}

// ---- Assistente de primeira execução ----

export type CompatStatus = 'recommended' | 'works' | 'untested' | 'unstable' | 'problematic' | 'unsupported'
export type CheckStatus = 'ok' | 'warning' | 'error' | 'pending' | 'info'

export interface LocalizedText {
  'pt-BR': string
  en: string
}

export interface WifiEntry {
  id: string
  model: string
  type: 'usb' | 'internal'
  chip: string
  drivers: string[]
  status: CompatStatus
  notes: LocalizedText
  source: string
  verifiedAt: string
}

export interface Esp32Entry {
  id: string
  model: string
  chip: string
  module: string | null
  connection: 'uart' | 'native_usb'
  flash: string
  status: CompatStatus
  notes: LocalizedText
  source: string
  verifiedAt: string
}

export interface HardwareCatalog {
  schema: number
  updatedAt: string
  notice: LocalizedText
  wifi: WifiEntry[]
  esp32: Esp32Entry[]
  usbSerial: { vid: string; pid: string; name: string; driverUrl: string | null }[]
}

export interface WifiCardInfo {
  phy: string
  interfaces: string[]
  driver: string | null
  module: string | null
  bus: string
  vendorId: string | null
  productId: string | null
  productName: string | null
  isInternetConnection: boolean
}

export interface SerialPortInfo {
  path: string
  vendorId: string | null
  productId: string | null
  productName: string | null
  driverMissing: boolean
}

export interface SetupCheck {
  id: string
  step: number
  status: CheckStatus
  essential: boolean
  code: string
  args: Record<string, string> | null
  fix: string | null
  availableIn: string | null
}

export interface CardTestResult {
  phy: string
  passed: boolean
  code: string
  at: string
  channels: { channel: number; set: boolean; sent: number; received: number; error: string | null }[]
  error: string | null
  simulated: boolean
  driver: string | null
}

export type SwitchTestCode =
  | 'found_decrypted'
  | 'found_not_decrypted'
  | 'other_session'
  | 'not_found'
  | 'monitor_failed'
  | 'channel_failed'
  | 'scan_failed'
  | 'engine_error'
  | 'cancelled'

/** Teste com o Switch (worker/traderock_worker/beacon_child.py). */
export interface SwitchTestResult {
  phy: string
  passed: boolean
  code: SwitchTestCode
  at: string
  networks: { commId: string; channel: number; protocol: number; frlg: boolean; participants: number; maxParticipants: number }[]
  channels: { channel: number; usable: boolean; error: string | null }[]
  undecryptable: number
  undecryptableChannels: number[] | null
  sweeps: number
  seconds: number
  error: string | null
  simulated: boolean
  driver: string | null
  keysSavedAt: string | null
  radio: 'wifi' | 'esp32'
}

export interface Esp32ProbeResult {
  port: string
  ok: boolean
  code: string
  target: string | null
  at: string
  simulated: boolean
}

export interface Esp32ChipResult {
  port: string
  target: string
  description: string
  revision: number
  flashSize: string | null
  at: string
  simulated: boolean
}

export interface Esp32FirmwareResult {
  port: string
  target: string
  matches: boolean
  written: boolean
  at: string
  simulated: boolean
}

export interface Esp32FlashJob {
  id: string
  port: string
  stage: 'identify' | 'write' | 'verify' | 'probe' | 'done' | 'error'
  done: number
  total: number
  errorCode: string | null
  errorMessage: string | null
  finished: boolean
}

export interface WorkerInstallInfo {
  flavor: 'release' | 'appimage' | 'dev'
  launcher: string
  policyInstalled: boolean
  safe: boolean
  pkexecFound: boolean
}

/** /api/worker: o componente de rede instalado e o embutido no AppImage. */
export interface WorkerInfo {
  install: WorkerInstallInfo | null
  bundled: { available: boolean; installed: boolean; upToDate: boolean }
  running: boolean
}

export interface SetupStatus {
  system: {
    os: 'linux' | 'windows' | 'other'
    isWsl: boolean
    virtualMachine: string | null
    distro: { id: string; idLike: string; name: string } | null
    wifiCards: WifiCardInfo[]
    serialPorts: SerialPortInfo[]
    brlttyRunning: boolean
    networkManagerRunning: boolean
    simulatedScenario: string | null
    worker: { flavor: 'release' | 'dev'; launcher: string; policyInstalled: boolean; safe: boolean; pkexecFound: boolean } | null
    ldnNetwork: { networkManager: boolean; udevLink: boolean; complete: boolean } | null
    missingTools: string[] | null
  }
  serialGroup: string
  advice: {
    situation: 'windows' | 'wsl' | 'vm' | 'no_wifi' | 'wifi_compatible' | 'wifi_test_first' | 'wifi_problematic'
    options: { engine: 'wifi' | 'esp32'; available: boolean; recommended: boolean; reasonCode: string | null }[]
    cards: { card: WifiCardInfo; status: CompatStatus; matches: WifiEntry[] }[]
    bestCard: { card: WifiCardInfo; status: CompatStatus; matches: WifiEntry[] } | null
    suggestedUsbAdapters: WifiEntry[]
  }
  state: {
    completed: boolean
    radio: 'wifi' | 'esp32' | null
    phy: string | null
    serialPort: string | null
    esp32Board: string | null
    esp32ModuleConfirmed: boolean
    keysSkipped: boolean
    cardTest: CardTestResult | null
    esp32Probe: Esp32ProbeResult | null
    esp32Chip: Esp32ChipResult | null
    esp32Firmware: Esp32FirmwareResult | null
    switchTest: SwitchTestResult | null
  }
  keys: {
    valid: boolean
    invalidLines: number[]
    presentRequired: string[]
    missingRequired: string[]
    protocols: number[]
    keyCount: number
    savedAt: string | null
  } | null
  checks: SetupCheck[]
  readyToTrade: boolean
  catalogUpdatedAt: string
  switchTestSeconds: number
  /** Só na simulação (desenvolvimento): resultados que o teste com o Switch pode devolver. */
  simulatedSwitchOutcomes: SwitchTestCode[] | null
}

// ---------- Modo servidor ----------

export type UserRole = 'user' | 'admin'

export interface Me {
  authenticated: boolean
  local: boolean
  id: string | null
  username: string | null
  role: UserRole | null
  csrf: string | null
  serverMode: boolean
}

export interface Account {
  id: string
  username: string | null
  role: UserRole
  disabled: boolean
  isOwner: boolean
  canLogin: boolean
  createdAt: string
  lastLoginAt: string | null
}

export interface LanInterface {
  name: string
  address: string
  wireless: boolean
  blocked: boolean
  virtual: boolean
}

export interface ServerModeStatus {
  enabled: boolean
  interface: string | null
  port: number
  url: string | null
  radio: 'wifi' | 'esp32' | 'none'
  interfaces: LanInterface[]
  sessions: number
}

export interface QueueItem {
  id: string
  name: string | null
  isHost: boolean
  position: number
  mine: boolean
  enqueuedAt: string
}

export interface TradeQueue {
  entries: QueueItem[]
  serverMode: boolean
}

export interface QueuedTrade {
  queued: true
  entryId: string
  position: number
}
