import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { markTradeRunning } from '../../components/Layout'
import { SwitchReady } from '../../components/illustrations/SwitchReady'
import { Button, Callout, Icon } from '../../components/ui'
import { ApiError, api } from '../../lib/api'
import { useConnection } from '../../lib/connection'
import { DEFAULT_TRAINER, trainerNameErrors } from '../../lib/trainer'
import type { Pokemon, QueuedTrade, StartTrade, TradeQueue, TradeSession, TradeSettings } from '../../lib/types'
import { useAuth } from '../../lib/auth'
import { takeTradeRequest } from '../../lib/tradeRequest'
import { QueueList, QueueStep } from './QueueStep'
import { useTradeSession } from '../../lib/useTradeSession'
import { LiveView } from './LiveView'
import { PartyStep, partyProblems, type PartyState } from './PartyStep'
import { TrainerStep, type TrainerState } from './TrainerStep'

const baseSteps = ['party', 'trainer', 'reminder'] as const
// No modo servidor, um passo a mais: entrar na fila e aguardar a vez.
const serverSteps = [...baseSteps, 'queue'] as const

const prepIcons = ['local_hospital', 'storefront', 'flag', 'devices']

export default function TradePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { session, events, connected, refresh } = useTradeSession()
  const { demo: simulation, ready } = useConnection()
  const [startedHere, setStartedHere] = useState<string | null>(null)
  const [step, setStep] = useState(0)
  const [pokemon, setPokemon] = useState<Pokemon[]>([])
  // "Usar na troca" no cofre: o Pokémon já entra no time, indo para o Switch.
  const [party, setParty] = useState<PartyState>(() => {
    const ids = takeTradeRequest()
    return { party: ids, offered: ids }
  })
  const [trainer, setTrainer] = useState<TrainerState>({
    game: 'fire_red',
    trainer: DEFAULT_TRAINER,
    scenario: 'success',
    failuresBeforeSuccess: 2,
  })
  const [startErrors, setStartErrors] = useState<string[]>([])
  const [acceptIllegal, setAcceptIllegal] = useState(false)
  const [allowIllegal, setAllowIllegal] = useState(false)
  const [starting, setStarting] = useState(false)
  const { me } = useAuth()
  const remote = me?.local === false
  const [queue, setQueue] = useState<TradeQueue | null>(null)
  const [queuedId, setQueuedId] = useState<string | null>(null)
  const [leaving, setLeaving] = useState(false)
  const [leftQueue, setLeftQueue] = useState(false)
  const serverMode = (me?.serverMode ?? false) || (queue?.serverMode ?? false)
  const wizardSteps = serverMode ? serverSteps : baseSteps

  const loadQueue = useCallback(async () => {
    try {
      const q = await api.get<TradeQueue>('/api/trades/queue')
      setQueue(q)
      return q
    } catch {
      return null
    }
  }, [])

  // Voltando à página com um pedido na fila (ou recarregando), o passo da fila reaparece.
  useEffect(() => {
    void loadQueue().then((q) => {
      const waiting = q?.entries.find((e) => e.mine && e.position > 0)
      if (waiting) {
        setQueuedId(waiting.id)
        setStep(3)
      }
    })
  }, [loadQueue])

  // Na fila: confere a vez a cada 2 s. Quando a troca começa, a tela da troca ao vivo aparece.
  useEffect(() => {
    if (!queuedId) return
    const timer = setInterval(async () => {
      const [q, current] = await Promise.all([loadQueue(), refresh().catch(() => null)])
      if (current?.status === 'running') {
        setStartedHere(current.id)
        setQueuedId(null)
      } else if (q && !q.entries.some((e) => e.id === queuedId)) {
        // Saiu da fila sem começar (um administrador tirou): volta ao lembrete.
        setQueuedId(null)
        setLeftQueue(true)
        setStep(2)
      }
    }, 2000)
    return () => clearInterval(timer)
  }, [queuedId, loadQueue, refresh])

  // No lembrete, mostra quem está na frente.
  useEffect(() => {
    if (!serverMode || step !== 2) return
    const timer = setInterval(() => void loadQueue(), 3000)
    return () => clearInterval(timer)
  }, [serverMode, step, loadQueue])

  const loadPokemon = useCallback(() => {
    api.get<Pokemon[]>('/api/pokemon').then(setPokemon).catch(() => undefined)
  }, [])

  useEffect(() => {
    loadPokemon()
    api.get<TradeSettings>('/api/settings/trade').then((s) => setAllowIllegal(s.allowIllegal)).catch(() => undefined)
  }, [loadPokemon])

  useEffect(() => markTradeRunning(session), [session])

  const showLive = session !== null && (session.status === 'running' || session.id === startedHere)

  if (showLive) {
    return (
      <LiveView
        session={session}
        events={events}
        connected={connected}
        onNewTrade={() => {
          setStartedHere(null)
          setStep(0)
          loadPokemon()
        }}
      />
    )
  }

  const byId = new Map(pokemon.map((p) => [p.id, p]))
  const offeredIllegal = party.offered.some((id) => byId.get(id)?.legality === 'illegal')
  const problems = partyProblems(party, offeredIllegal && !allowIllegal)
  const canGo = (target: number) =>
    target <= 0 || (problems.length === 0 && (target <= 1 || trainerNameErrors(trainer.trainer.name).length === 0))
  const canContinue = step < 2 ? canGo(step + 1) : !offeredIllegal || acceptIllegal

  async function start() {
    setStarting(true)
    setStartErrors([])
    const body: StartTrade = {
      party: party.party,
      offeredSlots: party.party.flatMap((id, index) => (party.offered.includes(id) ? [index] : [])),
      game: trainer.game,
      trainer: trainer.trainer,
      simulation: simulation ? { scenario: trainer.scenario, failuresBeforeSuccess: trainer.failuresBeforeSuccess } : null,
      acceptIllegal,
    }
    setLeftQueue(false)
    try {
      const result = await api.post<TradeSession | QueuedTrade>('/api/trades', body)
      if ('queued' in result) {
        setQueuedId(result.entryId)
        setStep(3)
        await loadQueue()
      } else {
        setStartedHere(result.id)
        await refresh()
      }
    } catch (e) {
      if (e instanceof ApiError) setStartErrors(e.errors.length > 0 ? e.errors : [e.code ?? 'unknown'])
      else setStartErrors(['unknown'])
    } finally {
      setStarting(false)
    }
  }

  const sending = party.party.filter((id) => party.offered.includes(id)).map((id) => byId.get(id))
  const sendNames = sending.map((p) => (p ? (p.nickname || p.speciesName).toUpperCase() : '?')).join(', ')
  const prepSteps = t('ui.trade.prep', { returnObjects: true }) as { title: string; sub: string }[]

  return (
    <div className="flex flex-col gap-5">
      {simulation && !ready && (
        <Callout tone="amber" icon="science">
          <span className="mr-2">
            <b>{t('demo.badge')}.</b> {t('ui.trade.demoBanner')}
          </span>
          {!remote && (
            <button type="button" onClick={() => navigate('/configuracoes?aba=conexao')} className="cursor-pointer font-bold underline">
              {t('ui.trade.setupConnection')}
            </button>
          )}
        </Callout>
      )}

      <div className="flex flex-wrap items-end justify-between gap-4">
        <h1 className="font-display text-[23px] font-bold md:text-[28px]">{t('ui.trade.title')}</h1>
        <ol className="flex max-w-full items-center gap-1.5 overflow-x-auto" aria-label="steps">
          {wizardSteps.map((s, i) => {
            const on = i === step
            const done = i < step
            const reachable = i < step || (i === step + 1 && canContinue && i < 3)
            return (
              <li key={s}>
                <button
                  type="button"
                  disabled={!reachable && !on}
                  onClick={() => reachable && step < 3 && setStep(i)}
                  aria-current={on ? 'step' : undefined}
                  className={`flex h-9 cursor-pointer items-center gap-2 rounded-full border py-0 pr-3.5 pl-1.5 text-sm font-semibold whitespace-nowrap disabled:cursor-default ${on ? 'border-accent bg-accent-soft text-ink' : 'border-line text-muted'}`}
                >
                  <span
                    className={`flex h-6 w-6 items-center justify-center rounded-full text-[13px] font-bold ${on || done ? 'bg-accent text-accent-ink' : 'bg-surface2 text-muted'}`}
                  >
                    {done ? '✓' : i + 1}
                  </span>
                  <span className={on ? '' : 'max-md:hidden'}>{t(`ui.trade.steps.${s}`)}</span>
                </button>
              </li>
            )
          })}
        </ol>
      </div>

      {step === 0 && <PartyStep pokemon={pokemon} state={party} onChange={setParty} onReload={loadPokemon} allowIllegal={allowIllegal} />}
      {step === 1 && <TrainerStep state={trainer} onChange={setTrainer} simulation={simulation} />}
      {step === 2 && (
        <div className="flex flex-col gap-[18px] rounded-xl border border-line bg-surface p-[22px]">
          <div>
            <div className="text-lg font-bold">{t('trade.reminder.title')}</div>
            <div className="text-sm text-muted">{t('ui.trade.prepSub')}</div>
          </div>
          <SwitchReady labels={[t('trade.reminder.illustration.center'), t('trade.reminder.illustration.counter'), t('trade.reminder.illustration.leader')]} />
          <div className="grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(180px,1fr))]">
            {prepSteps.map((p, i) => (
              <div key={p.title} className="flex gap-2.5 rounded-lg bg-surface2 p-3">
                <span className="flex h-[26px] w-[26px] shrink-0 items-center justify-center rounded-full bg-accent text-[13px] font-bold text-accent-ink">{i + 1}</span>
                <div className="text-sm leading-snug">
                  <b className="flex items-center gap-1.5">
                    {p.title}
                    <Icon name={prepIcons[i]} className="text-[17px] text-muted" />
                  </b>
                  <span className="text-muted">{p.sub}</span>
                </div>
              </div>
            ))}
          </div>
          <details className="text-sm">
            <summary className="cursor-pointer font-semibold text-accent">{t('ui.trade.learnMore')}</summary>
            <p className="mt-2 text-muted">{t('ui.trade.prepMore')}</p>
          </details>

          {offeredIllegal && (
            <label className="flex items-start gap-2 rounded-lg bg-bad-soft p-3 text-sm text-bad">
              <input type="checkbox" className="mt-0.5 accent-[var(--tr-bad)]" checked={acceptIllegal} onChange={(e) => setAcceptIllegal(e.target.checked)} />
              {t('trade.party.acceptIllegal')}
            </label>
          )}
          {serverMode && queue && (
            <Callout tone="sky" icon="groups">
              <p>
                {remote ? t('ui.trade.queueNote') : ''}{' '}
                <b>{queue.entries.length === 0 ? t('queue.freeNow') : t('queue.ahead', { count: queue.entries.length })}</b>
              </p>
              {queue.entries.length > 0 && (
                <div className="mt-2">
                  <QueueList queue={queue} />
                </div>
              )}
            </Callout>
          )}
          {leftQueue && <Callout tone="amber">{t('queue.removed')}</Callout>}
          {startErrors.length > 0 && (
            <Callout tone="red" icon="error">
              <p>{t('trade.startError')}</p>
              <ul className="list-disc pl-5">
                {startErrors.map((e) => (
                  <li key={e}>{t(`errors.${e}`, { defaultValue: t('failures.unknown') })}</li>
                ))}
              </ul>
            </Callout>
          )}
          <div className="flex items-center gap-3 rounded-lg bg-surface2 px-3.5 py-3">
            <Icon name="swap_horiz" className="text-[22px] text-accent" />
            <div className="flex-1 text-sm">
              <b>{sending.length ? t('ui.trade.sending', { names: sendNames }) : t('ui.trade.nothingToSend')}</b> · {t(`trade.trainer.${trainer.game}`)} ·{' '}
              {t('ui.trade.trainerLabel')} <b>{trainer.trainer.name}</b>
            </div>
          </div>
        </div>
      )}

      {step === 3 && queuedId && (
        <QueueStep
          queue={queue}
          entryId={queuedId}
          leaving={leaving}
          onLeave={() => {
            setLeaving(true)
            void api
              .delete(`/api/trades/queue/${queuedId}`)
              .catch(() => undefined)
              .finally(() => {
                setLeaving(false)
                setQueuedId(null)
                setStep(2)
                void loadQueue()
              })
          }}
        />
      )}

      {step < 3 && (
        <div className="flex items-center justify-between">
          <Button variant="secondary" size="lg" disabled={step === 0} onClick={() => setStep(step - 1)}>
            {t('common.back')}
          </Button>
          {step < 2 ? (
            <Button size="lg" iconAfter="arrow_forward" disabled={!canContinue} onClick={() => setStep(step + 1)}>
              {t('ui.trade.continue')}
            </Button>
          ) : (
            <Button size="lg" iconAfter="play_arrow" disabled={starting || !canContinue} onClick={() => void start()}>
              {serverMode && queue && queue.entries.length > 0 ? t('queue.join') : t('trade.reminder.start')}
            </Button>
          )}
        </div>
      )}
    </div>
  )
}
