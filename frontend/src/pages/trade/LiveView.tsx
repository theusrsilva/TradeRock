import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { MarkdownText } from '../../components/Markdown'
import { PokemonCard } from '../../components/PokemonCard'
import { TradeRoom } from '../../components/illustrations/TradeRoom'
import { Badge, Button, Card, Modal } from '../../components/ui'
import { api } from '../../lib/api'
import { contentSections, interpolate } from '../../lib/content'
import { instructionFor, receivedIds, timeline } from '../../lib/tradeProgress'
import type { Pokemon, TradeEvent, TradeSession } from '../../lib/types'

const statusTone = {
  running: 'sky',
  completed: 'emerald',
  partially_completed: 'amber',
  failed: 'red',
  cancelled: 'slate',
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
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [cancelling, setCancelling] = useState(false)
  const [received, setReceived] = useState<Record<string, Pokemon>>({})

  const running = session.status === 'running'
  const phases = useMemo(() => contentSections(i18n.language, 'trade/fases'), [i18n.language])
  const instruction = instructionFor(events, session.tradesPlanned)
  const failedSession = session.status === 'failed' || session.status === 'partially_completed' || session.status === 'cancelled'
  const steps = timeline(events, session.tradesPlanned, !running, failedSession)
  const ids = receivedIds(events)
  const lastRetry = [...events].reverse().find((e) => e.kind === 'retry_scheduled')
  const lastError = [...events].reverse().find((e) => e.kind === 'error')

  // Busca os cards dos recebidos conforme chegam.
  useEffect(() => {
    for (const id of ids) {
      if (id && !received[id]) {
        api
          .get<Pokemon>(`/api/pokemon/${id}`)
          .then((p) => setReceived((r) => ({ ...r, [id]: p })))
          .catch(() => undefined)
      }
    }
  }, [ids, received])

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

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-xl font-semibold">{running ? t('trade.wizard.live') : t(`trade.result.${session.status}`)}</h1>
        {running && <Badge tone={statusTone.running}>{t('trade.result.running')}</Badge>}
        {session.isSimulation && <Badge tone="amber">{t('demo.badge')}</Badge>}
        {session.attempt > 0 && <span className="text-sm text-slate-500">{t('trade.live.attempt', { attempt: session.attempt, max: session.maxAttempts })}</span>}
        {running && !connected && <span className="text-sm text-amber-700 dark:text-amber-400">{t('trade.live.sseLost')}</span>}
      </div>

      <div className="grid gap-4 lg:grid-cols-[1fr_18rem]">
        <div className="space-y-4">
          {running ? (
            <Card className="space-y-3 border-sky-300 dark:border-sky-700">
              <h2 className="text-sm font-semibold uppercase tracking-wide text-sky-700 dark:text-sky-400">{t('trade.live.nowWhat')}</h2>
              <MarkdownText
                className="text-base"
                text={interpolate(phases[instruction.key] ?? '', {
                  trainer: session.trainerName,
                  n: instruction.n,
                  total: session.tradesPlanned,
                })}
              />
              {instruction.key === 'andar_cadeira' && (
                <TradeRoom leftLabel={t('trade.live.leftChair')} rightLabel={session.trainerName} />
              )}
              {lastRetry && instruction.key === 'tentando_de_novo' && lastRetry.retryInSeconds !== null && (
                <p className="text-sm text-slate-500">{t('trade.live.retryIn', { seconds: lastRetry.retryInSeconds })}</p>
              )}
              {lastError && instruction.key === 'tentando_de_novo' && lastError.errorCode && (
                <p className="text-sm text-slate-500">{t(`failures.${lastError.errorCode}`, { defaultValue: lastError.message ?? '' })}</p>
              )}
              <div className="pt-2">
                <Button variant="danger" disabled={cancelling} onClick={() => setConfirmCancel(true)}>
                  {cancelling ? t('trade.live.cancelling') : t('trade.live.cancel')}
                </Button>
              </div>
            </Card>
          ) : (
            <Card className="space-y-3">
              {failure && session.status !== 'completed' && <p className="text-sm">{failure}</p>}
              <div className="flex flex-wrap gap-2">
                <Button onClick={onNewTrade}>{t('trade.result.newTrade')}</Button>
                <Link to="/historico" className="inline-flex items-center rounded-lg px-4 py-2 text-sm text-sky-700 hover:underline dark:text-sky-400">
                  {t('trade.result.history')}
                </Link>
              </div>
            </Card>
          )}

          {ids.some(Boolean) && (
            <Card className="space-y-2">
              <h2 className="font-semibold">{running ? t('trade.live.received') : t('trade.result.receivedTitle')}</h2>
              {ids.map((id, i) =>
                id && received[id] ? <PokemonCard key={`${id}-${i}`} pokemon={received[id]} /> : null,
              )}
            </Card>
          )}

          <details className="rounded-xl border border-slate-200 bg-white p-4 text-sm dark:border-slate-800 dark:bg-slate-900">
            <summary className="cursor-pointer font-medium">{t('trade.live.log')}</summary>
            <ol className="mt-3 max-h-72 space-y-1 overflow-y-auto font-mono text-xs">
              {events.map((e) => (
                <li key={e.seq} className={e.kind === 'error' ? 'text-red-700 dark:text-red-400' : 'text-slate-600 dark:text-slate-300'}>
                  <span className="text-slate-400">{new Date(e.at).toLocaleTimeString(i18n.language)}</span> [{e.kind}] {e.message}
                </li>
              ))}
            </ol>
          </details>
        </div>

        <Card>
          <ol className="space-y-3">
            {steps.map((step) => (
              <li key={step.id} className="flex items-center gap-3 text-sm">
                <span
                  className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-bold ${
                    step.state === 'done'
                      ? 'bg-emerald-500 text-white'
                      : step.state === 'current'
                        ? 'animate-pulse bg-sky-500 text-white'
                        : step.state === 'failed'
                          ? 'bg-red-500 text-white'
                          : 'bg-slate-200 text-slate-500 dark:bg-slate-700'
                  }`}
                  aria-hidden
                >
                  {step.state === 'done' ? '✓' : step.state === 'failed' ? '✕' : ''}
                </span>
                <span
                  className={
                    step.state === 'pending'
                      ? 'text-slate-400'
                      : step.state === 'current'
                        ? 'font-semibold'
                        : step.state === 'failed'
                          ? 'font-semibold text-red-700 dark:text-red-400'
                          : ''
                  }
                >
                  {t(step.label, { n: step.tradeNumber })}
                </span>
              </li>
            ))}
          </ol>
        </Card>
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
