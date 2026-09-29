import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { markTradeRunning } from '../../components/Layout'
import { ContentFile } from '../../components/Markdown'
import { SwitchReady } from '../../components/illustrations/SwitchReady'
import { Button, Card } from '../../components/ui'
import { ApiError, api } from '../../lib/api'
import { DEFAULT_TRAINER, trainerNameErrors } from '../../lib/trainer'
import type { Engine, Pokemon, StartTrade, TradeSession } from '../../lib/types'
import { useTradeSession } from '../../lib/useTradeSession'
import { LiveView } from './LiveView'
import { PartyStep, partyProblems, type PartyState } from './PartyStep'
import { TrainerStep, type TrainerState } from './TrainerStep'

const wizardSteps = ['party', 'trainer', 'reminder'] as const

export default function TradePage() {
  const { t } = useTranslation()
  const { session, events, connected, refresh } = useTradeSession()
  const [startedHere, setStartedHere] = useState<string | null>(null)
  const [step, setStep] = useState(0)
  const [pokemon, setPokemon] = useState<Pokemon[]>([])
  const [simulation, setSimulation] = useState(false)
  const [party, setParty] = useState<PartyState>({ party: [], offered: [] })
  const [trainer, setTrainer] = useState<TrainerState>({
    game: 'fire_red',
    trainer: DEFAULT_TRAINER,
    scenario: 'success',
    failuresBeforeSuccess: 2,
  })
  const [startErrors, setStartErrors] = useState<string[]>([])
  const [starting, setStarting] = useState(false)

  const loadPokemon = useCallback(() => {
    api.get<Pokemon[]>('/api/pokemon').then(setPokemon).catch(() => undefined)
  }, [])

  useEffect(() => {
    loadPokemon()
    api
      .get<Engine[]>('/api/engines')
      .then((engines) => setSimulation(engines.find((e) => e.selected)?.isSimulation ?? false))
      .catch(() => undefined)
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

  const canContinue =
    step === 0 ? partyProblems(party).length === 0 : step === 1 ? trainerNameErrors(trainer.trainer.name).length === 0 : true

  async function start() {
    setStarting(true)
    setStartErrors([])
    const body: StartTrade = {
      party: party.party,
      offeredSlots: party.party.flatMap((id, index) => (party.offered.includes(id) ? [index] : [])),
      game: trainer.game,
      trainer: trainer.trainer,
      simulation: simulation ? { scenario: trainer.scenario, failuresBeforeSuccess: trainer.failuresBeforeSuccess } : null,
    }
    try {
      const snapshot = await api.post<TradeSession>('/api/trades', body)
      setStartedHere(snapshot.id)
      await refresh()
    } catch (e) {
      if (e instanceof ApiError) setStartErrors(e.errors.length > 0 ? e.errors : [e.code ?? 'unknown'])
      else setStartErrors(['unknown'])
    } finally {
      setStarting(false)
    }
  }

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">{t('trade.title')}</h1>

      <ol className="flex flex-wrap gap-2 text-sm" aria-label="steps">
        {wizardSteps.map((s, i) => (
          <li
            key={s}
            className={`rounded-full px-3 py-1 ${
              i === step
                ? 'bg-sky-600 text-white'
                : i < step
                  ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/50 dark:text-emerald-300'
                  : 'bg-slate-200 text-slate-600 dark:bg-slate-800 dark:text-slate-400'
            }`}
          >
            {i + 1}. {t(`trade.wizard.${s}`)}
          </li>
        ))}
      </ol>

      {step === 0 && <PartyStep pokemon={pokemon} state={party} onChange={setParty} onReload={loadPokemon} />}
      {step === 1 && <TrainerStep state={trainer} onChange={setTrainer} simulation={simulation} />}
      {step === 2 && (
        <Card className="space-y-4">
          <h2 className="font-semibold">{t('trade.reminder.title')}</h2>
          <SwitchReady
            labels={[
              t('trade.reminder.illustration.center'),
              t('trade.reminder.illustration.counter'),
              t('trade.reminder.illustration.leader'),
            ]}
          />
          <ContentFile name="trade/lembrete" />
          {startErrors.length > 0 && (
            <div className="rounded-lg bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-300">
              <p>{t('trade.startError')}</p>
              <ul className="list-disc pl-5">
                {startErrors.map((e) => (
                  <li key={e}>{t(`errors.${e}`, { defaultValue: t('failures.unknown') })}</li>
                ))}
              </ul>
            </div>
          )}
        </Card>
      )}

      {step === 0 && partyProblems(party).length > 0 && (
        <ul className="text-sm text-slate-500">
          {partyProblems(party).map((p) => (
            <li key={p}>• {t(p)}</li>
          ))}
        </ul>
      )}

      <div className="flex justify-between">
        <Button variant="secondary" disabled={step === 0} onClick={() => setStep(step - 1)}>
          {t('common.back')}
        </Button>
        {step < 2 ? (
          <Button disabled={!canContinue} onClick={() => setStep(step + 1)}>
            {t('common.next')}
          </Button>
        ) : (
          <Button disabled={starting} onClick={() => void start()}>
            {t('trade.reminder.start')}
          </Button>
        )}
      </div>
    </div>
  )
}
