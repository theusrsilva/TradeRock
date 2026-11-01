import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { ContentFile, MarkdownText } from '../../components/Markdown'
import { TradeRoom } from '../../components/illustrations/TradeRoom'
import { Badge, Button, Callout, Icon, Modal } from '../../components/ui'
import { api, spriteUrl } from '../../lib/api'
import { useConnection } from '../../lib/connection'
import { contentSections, interpolate } from '../../lib/content'
import { gameOf } from '../../lib/games'
import { instructionFor, receivedIds, timeline, type InstructionKey } from '../../lib/tradeProgress'
import type { Pokemon, TradeEvent, TradeHistoryItem, TradeSession } from '../../lib/types'

const instructionIcon: Record<InstructionKey, string> = {
  procurando: 'wifi_find',
  tentando_de_novo: 'refresh',
  aprovar: 'how_to_reg',
  andar_cadeira: 'chair',
  aguarde: 'hourglass_top',
  escolher: 'touch_app',
  confirmar: 'sync_alt',
  proxima: 'redo',
  sair: 'logout',
  cancelado_switch: 'block',
  fim: 'check',
  sala_abrindo: 'wifi_tethering',
  sala_aberta: 'pin',
  sala_entrou: 'touch_app',
  sala_proxima: 'redo',
  sala_sair: 'logout',
  sala_saiu: 'block',
}

const resultLook = {
  completed: { icon: 'check', cls: 'bg-ok-soft text-ok' },
  partially_completed: { icon: 'rule', cls: 'bg-warn-soft text-warn' },
  failed: { icon: 'close', cls: 'bg-bad-soft text-bad' },
  cancelled: { icon: 'block', cls: 'bg-surface2 text-muted' },
  running: { icon: 'sync', cls: 'bg-info-soft text-info' },
} as const

export function LiveView({
  session,
  events,
  connected,
  onNewTrade,
}: {
  session: TradeSession
  events: TradeEvent[]
  connected: boolean
  onNewTrade: () => void
}) {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [cancelling, setCancelling] = useState(false)
  const [pokemon, setPokemon] = useState<Record<string, Pokemon>>({})
  const [record, setRecord] = useState<TradeHistoryItem | null>(null)
  const [logOpen, setLogOpen] = useState(false)
  const asked = useRef(new Set<string>())

  const running = session.status === 'running'
  const phases = useMemo(() => contentSections(i18n.language, 'trade/fases'), [i18n.language])
  // O fluxo da tela depende de quem abre a sala (D30): pelo pokeldn (jogos novos, e o FRLG pelo ESP32) o TradeRock é
  // o anfitrião e o Switch entra com o código; pela placa Wi-Fi no FRLG, o TradeRock entra na sala do Switch.
  const { engines } = useConnection()
  const engine = engines.find((e) => e.id === session.engineId)
  const family = gameOf(session.game).family
  const hosting = !session.isSimulation && (engine?.pokeldnGames.includes(family) ?? family !== 'frlg')
  const lan = engine?.ignoresLinkCode ?? false
  const instruction = instructionFor(events, session.tradesPlanned, hosting)
  const failedSession = session.status === 'failed' || session.status === 'partially_completed' || session.status === 'cancelled'
  const steps = timeline(events, session.tradesPlanned, !running, failedSession, hosting)
  const ids = receivedIds(events)
  const lastRetry = [...events].reverse().find((e) => e.kind === 'retry_scheduled')
  const lastError = [...events].reverse().find((e) => e.kind === 'error')

  // Terminada: o registro do histórico diz quem foi enviado em cada troca.
  useEffect(() => {
    if (running) return
    api
      .get<{ trade: TradeHistoryItem }>(`/api/trades/${session.id}`)
      .then((r) => setRecord(r.trade))
      .catch(() => undefined)
  }, [running, session.id])

  // Busca os Pokémon (recebidos conforme chegam e, no fim, os enviados).
  const wanted = [...ids.filter((id): id is string => !!id), ...(record?.entries.map((e) => e.sentPokemonId) ?? [])].join(',')
  useEffect(() => {
    for (const id of wanted.split(',').filter(Boolean)) {
      if (!asked.current.has(id)) {
        asked.current.add(id)
        api
          .get<Pokemon>(`/api/pokemon/${id}`)
          .then((p) => setPokemon((r) => ({ ...r, [id]: p })))
          .catch(() => undefined)
      }
    }
  }, [wanted])

  async function cancel() {
    setCancelling(true)
    try {
      await api.post('/api/trades/current/cancel')
    } finally {
      setCancelling(false)
      setConfirmCancel(false)
    }
  }

  const failure = session.failureCode ? t(`failures.${session.failureCode}`, { defaultValue: session.failureMessage ?? '' }) : null
  const code = lan ? t('ui.live.anyCode') : session.linkCode || t('ui.live.noCode')
  const text = interpolate(phases[instruction.key] ?? '', { trainer: session.trainerName, n: instruction.n, total: session.tradesPlanned, code })
  const look = resultLook[session.status]
  const sent = (record?.entries ?? []).map((e) => pokemon[e.sentPokemonId]).filter((p): p is Pokemon => !!p)
  const received = ids.filter((id): id is string => !!id).map((id) => pokemon[id]).filter((p): p is Pokemon => !!p)
  const firstReceived = ids.find((id) => !!id)

  return (
    <div className="mx-auto flex w-full max-w-[880px] flex-col gap-5">
      <div className="flex flex-wrap items-center gap-2.5">
        <h1 className="flex-1 font-display text-[23px] font-bold md:text-[28px]">{running ? t('ui.live.running') : t(`trade.result.${session.status}`)}</h1>
        {session.isSimulation && <Badge tone="amber">{t('demo.badge')}</Badge>}
        {session.attempt > 0 && <span className="text-[13px] text-muted">{t('trade.live.attempt', { attempt: session.attempt, max: session.maxAttempts })}</span>}
      </div>
      {running && !connected && <Callout tone="amber" icon="wifi_off">{t('trade.live.sseLost')}</Callout>}

      {running ? (
        <div className="flex flex-col items-center gap-4 rounded-xl border border-line bg-surface px-7 py-9 text-center">
          <div className="relative h-[88px] w-[88px]">
            <div className="tr-pulse absolute inset-0 rounded-full bg-accent" />
            <div className="absolute inset-0 flex items-center justify-center rounded-full bg-accent text-accent-ink">
              <Icon name={instructionIcon[instruction.key]} className="text-[42px]" />
            </div>
          </div>
          <div className="text-xs font-bold tracking-[1px] text-accent uppercase">{t('trade.live.nowWhat')}</div>
          <MarkdownText className="max-w-[560px] text-[19px] leading-snug [&_strong]:font-display [&_strong]:text-[24px]" text={text} />
          {instruction.key === 'andar_cadeira' && <TradeRoom leftLabel={t('trade.live.leftChair')} rightLabel={session.trainerName} />}
          {instruction.key === 'sala_aberta' && (
            <div className="w-full max-w-[560px] rounded-lg bg-surface2 p-4 text-left text-sm">
              <ContentFile name={lan ? 'trade/jogos/swsh-lan' : `trade/jogos/${family}`} />
            </div>
          )}
          {lastRetry && instruction.key === 'tentando_de_novo' && lastRetry.retryInSeconds !== null && (
            <p className="text-sm text-muted">{t('trade.live.retryIn', { seconds: lastRetry.retryInSeconds })}</p>
          )}
          {lastError && instruction.key === 'tentando_de_novo' && lastError.errorCode && (
            <p className="text-sm text-muted">{t(`failures.${lastError.errorCode}`, { defaultValue: lastError.message ?? '' })}</p>
          )}
          {received.length > 0 && (
            <div className="flex flex-wrap justify-center gap-2">
              {received.map((p, i) => (
                <PokemonTile key={`${p.id}-${i}`} pokemon={p} highlight />
              ))}
            </div>
          )}
          <Button variant="outlineDanger" className="mt-1.5" disabled={cancelling} onClick={() => setConfirmCancel(true)}>
            {cancelling ? t('trade.live.cancelling') : t('trade.live.cancel')}
          </Button>
        </div>
      ) : (
        <div className="flex flex-col items-center gap-[18px] rounded-xl border border-line bg-surface px-7 py-8 text-center">
          <div className={`flex h-16 w-16 items-center justify-center rounded-full ${look.cls}`}>
            <Icon name={look.icon} className="text-[38px]" />
          </div>
          <div className="font-display text-[26px] font-bold">{t(`trade.result.${session.status}`)}</div>
          {failure && session.status !== 'completed' && <p className="max-w-[520px] text-sm text-muted">{failure}</p>}
          {(sent.length > 0 || received.length > 0) && (
            <div className="flex flex-wrap items-center justify-center gap-[22px]">
              <div className="flex flex-col items-center gap-1">
                <div className="text-xs tracking-[.8px] text-muted uppercase">{t('history.sent')}</div>
                <div className="flex flex-wrap gap-1.5">
                  {sent.map((p) => (
                    <PokemonTile key={p.id} pokemon={p} />
                  ))}
                </div>
              </div>
              <Icon name="sync_alt" className="text-[30px] text-muted" />
              <div className="flex flex-col items-center gap-1">
                <div className="text-xs tracking-[.8px] text-muted uppercase">{t('history.received')}</div>
                <div className="flex flex-wrap gap-1.5">
                  {received.length > 0 ? received.map((p, i) => <PokemonTile key={`${p.id}-${i}`} pokemon={p} highlight />) : <span className="text-sm text-muted">{t('history.nothing')}</span>}
                </div>
              </div>
            </div>
          )}
          {received.length > 0 && <div className="text-sm text-muted">{t('ui.live.savedInbox')}</div>}
          <div className="flex flex-wrap justify-center gap-2.5">
            {firstReceived && (
              <Button size="lg" onClick={() => navigate(`/pokemon?pokemon=${firstReceived}`)}>
                {t('ui.live.seeInVault')}
              </Button>
            )}
            <Button size="lg" variant={firstReceived ? 'secondary' : 'primary'} onClick={onNewTrade}>
              {t('trade.result.newTrade')}
            </Button>
            <Button size="lg" variant="ghost" onClick={() => navigate('/historico')}>
              {t('trade.result.history')}
            </Button>
          </div>
        </div>
      )}

      <div className="grid gap-2 [grid-template-columns:repeat(auto-fit,minmax(104px,1fr))]">
        {steps.map((step) => {
          const bar = step.state === 'done' ? 'bg-ok' : step.state === 'current' ? 'bg-accent' : step.state === 'failed' ? 'bg-bad' : 'bg-surface2'
          const color =
            step.state === 'done' ? 'text-ok' : step.state === 'current' ? 'font-bold text-ink' : step.state === 'failed' ? 'font-bold text-bad' : 'text-muted'
          const icon = step.state === 'done' ? 'check_circle' : step.state === 'current' ? 'radio_button_checked' : step.state === 'failed' ? 'cancel' : 'radio_button_unchecked'
          return (
            <div key={step.id} className="flex flex-col gap-2">
              <div className={`h-1.5 rounded-sm ${bar}`} />
              <div className={`flex items-center gap-1.5 text-[12.5px] ${color}`}>
                <Icon name={icon} className="text-base" />
                {t(step.label, { n: step.tradeNumber })}
              </div>
            </div>
          )
        })}
      </div>

      <div className="rounded-xl border border-line bg-surface">
        <button
          type="button"
          onClick={() => setLogOpen(!logOpen)}
          aria-expanded={logOpen}
          className="flex w-full cursor-pointer items-center gap-2 px-[18px] py-3.5 text-sm font-semibold text-muted"
        >
          <Icon name={logOpen ? 'expand_less' : 'expand_more'} />
          {t('ui.live.techDetails')}
        </button>
        {logOpen && (
          <ol className="flex max-h-72 flex-col gap-[3px] overflow-y-auto px-[18px] pb-4 font-mono text-[12.5px] text-muted">
            {events.map((e) => (
              <li key={e.seq} className={e.kind === 'error' ? 'text-bad' : ''}>
                {new Date(e.at).toLocaleTimeString(i18n.language)} [{e.kind}] {e.message}
              </li>
            ))}
          </ol>
        )}
      </div>

      {confirmCancel && (
        <Modal
          title={t('trade.live.cancel')}
          actions={
            <>
              <Button variant="secondary" onClick={() => setConfirmCancel(false)}>
                {t('trade.live.cancelNo')}
              </Button>
              <Button variant="danger" disabled={cancelling} onClick={() => void cancel()}>
                {t('trade.live.cancelYes')}
              </Button>
            </>
          }
        >
          {t('trade.live.cancelConfirm')}
        </Modal>
      )}
    </div>
  )
}

function PokemonTile({ pokemon, highlight = false }: { pokemon: Pokemon; highlight?: boolean }) {
  return (
    <div className={`rounded-lg p-2 text-center ${highlight ? 'bg-accent-soft' : 'bg-tile'}`}>
      <img src={spriteUrl(pokemon.species)} alt="" className="pixelated block h-[72px] w-[72px]" />
      <div className="max-w-[72px] truncate text-xs font-bold">{(pokemon.nickname || pokemon.speciesName).toUpperCase()}</div>
    </div>
  )
}
