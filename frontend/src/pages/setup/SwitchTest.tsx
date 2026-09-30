import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Badge, Button, inputClass } from '../../components/ui'
import { api, ApiError } from '../../lib/api'
import type { SetupStatus, SwitchTestCode, SwitchTestResult } from '../../lib/types'

/**
 * Etapa 6: o teste com o Switch. O rádio (placa Wi-Fi pelo worker, ou o ESP32 pela serial) escuta os
 * anúncios da sala (aberta como Leader) e tenta decifrá-los com o prod.keys, sem entrar nela. Cancelar
 * aborta o pedido; com a placa Wi-Fi, o app avisa o worker, que para e devolve o rádio.
 */
export function SwitchTest({ status, onStatus }: { status: SetupStatus; onStatus: (s: SetupStatus) => void }) {
  const { t } = useTranslation()
  const { state, advice, checks, keys } = status
  const [running, setRunning] = useState(false)
  const [elapsed, setElapsed] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [cancelled, setCancelled] = useState(false)
  const [simulated, setSimulated] = useState<SwitchTestCode>('found_decrypted')
  const abort = useRef<AbortController | null>(null)

  useEffect(() => () => abort.current?.abort(), [])
  useEffect(() => {
    if (!running) return
    const started = Date.now()
    const timer = setInterval(() => setElapsed(Math.floor((Date.now() - started) / 1000)), 500)
    return () => clearInterval(timer)
  }, [running])

  const esp32 = state.radio === 'esp32'
  const card = advice.cards.find((c) => c.card.phy === state.phy)
  const cardDriver = card ? (card.card.module ?? card.card.driver) : null
  const probe = esp32 && state.esp32Probe?.ok && state.esp32Probe.port === state.serialPort ? state.esp32Probe : null
  // O resultado vale para este rádio (placa ou porta + chip) e este prod.keys (o backend confere o mesmo).
  const sw = state.switchTest
  const sameKeys = !!sw && keys?.valid === true && sw.keysSavedAt === keys.savedAt
  const test = !sw || !sameKeys ? null
    : esp32 ? (sw.radio === 'esp32' && sw.phy === state.serialPort && sw.driver === probe?.target ? sw : null)
    : (sw.radio !== 'esp32' && sw.phy === state.phy && (!sw.driver || sw.driver === cardDriver) ? sw : null)
  const check = checks.find((c) => c.id === 'switch_test')
  const radioOk = esp32 ? !!probe : checks.find((c) => c.id === 'card_test')?.status === 'ok'
  const keysOk = keys?.valid === true
  const canRun = (esp32 ? !!state.serialPort : !!card) && radioOk && keysOk && !running

  async function run() {
    const controller = new AbortController()
    abort.current = controller
    setRunning(true)
    setElapsed(0)
    setError(null)
    setCancelled(false)
    try {
      const simulatedOutcome = status.simulatedSwitchOutcomes ? simulated : null
      const res = esp32
        ? await api.post<{ result: SwitchTestResult; status: SetupStatus }>('/api/setup/esp32/switch-test', { simulatedOutcome }, controller.signal)
        : await api.post<{ result: SwitchTestResult; status: SetupStatus }>('/api/setup/switch-test', { phy: state.phy, simulatedOutcome }, controller.signal)
      onStatus(res.status)
    } catch (e) {
      if (controller.signal.aborted) setCancelled(true)
      else setError(e instanceof ApiError && e.code ? e.code : 'unknown')
    } finally {
      abort.current = null
      setRunning(false)
    }
  }

  if (esp32 ? !state.serialPort : !card) return null

  return (
    <div className="space-y-3 rounded-xl border border-slate-200 p-4 dark:border-slate-800">
      <h3 className="font-semibold">{t('wizard.switchTest.title')}</h3>
      <p className="text-sm">{t('wizard.switchTest.before')}</p>
      <ol className="list-decimal space-y-1 pl-5 text-sm">
        <li>{t('wizard.switchTest.stepOpen')}</li>
        <li>{t('wizard.switchTest.stepLeader')}</li>
        <li>{t('wizard.switchTest.stepNear')}</li>
      </ol>
      <ul className="list-disc space-y-1 pl-5 text-sm text-slate-600 dark:text-slate-300">
        <li>{t('wizard.switchTest.noJoin')}</li>
        {esp32 ? <li>{t('wizard.switchTest.esp32Band')}</li> : <li>{t('wizard.switchTest.offline', { seconds: status.switchTestSeconds })}</li>}
      </ul>

      {!keysOk && <p className="text-sm text-amber-700 dark:text-amber-300">{t('wizard.switchTest.needsKeys')}</p>}
      {keysOk && !radioOk && (
        <p className="text-sm text-amber-700 dark:text-amber-300">{t(esp32 ? 'wizard.switchTest.needsFirmware' : 'wizard.switchTest.needsCard')}</p>
      )}

      {status.simulatedSwitchOutcomes && (
        <label className="flex flex-wrap items-center gap-2 text-xs">
          <Badge tone="amber">{t('wizard.simulation')}</Badge>
          {t('wizard.switchTest.simulate')}
          <select className={`${inputClass} w-auto! py-1`} value={simulated} onChange={(e) => setSimulated(e.target.value as SwitchTestCode)}>
            {status.simulatedSwitchOutcomes.map((o) => (
              <option key={o} value={o}>
                {o}
              </option>
            ))}
          </select>
        </label>
      )}

      <div className="flex flex-wrap items-center gap-2">
        {running ? (
          <>
            <Button disabled>{t('wizard.switchTest.running', { seconds: elapsed })}</Button>
            <Button variant="secondary" onClick={() => abort.current?.abort()}>
              {t('wizard.switchTest.cancel')}
            </Button>
          </>
        ) : (
          <Button onClick={() => void run()} disabled={!canRun}>
            {test ? t('wizard.switchTest.again') : t('wizard.switchTest.run')}
          </Button>
        )}
        {running && !esp32 && <span className="text-sm text-slate-500">{t('wizard.switchTest.waitPassword')}</span>}
      </div>
      {running && (
        <div className="h-1.5 overflow-hidden rounded-full bg-slate-200 dark:bg-slate-800" aria-hidden>
          <div
            className="h-full bg-sky-600 transition-all"
            style={{ width: `${Math.min(100, (elapsed / status.switchTestSeconds) * 100)}%` }}
          />
        </div>
      )}

      {cancelled && <p className="text-sm text-slate-500">{t('wizard.switchTest.codes.cancelled')}</p>}
      {error && (
        <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-200">
          {t(`failures.${error}`, { defaultValue: t('failures.unknown') })}
        </p>
      )}

      {test && !running && <SwitchTestView test={test} hint={check?.args?.hint} />}
    </div>
  )
}

function SwitchTestView({ test, hint }: { test: SwitchTestResult; hint?: string }) {
  const { t, i18n } = useTranslation()
  const tips = t(`wizard.switchTest.tips.${test.code}`, { returnObjects: true, defaultValue: [] }) as string[] | string
  const unusable = test.channels.filter((c) => !c.usable).map((c) => c.channel)
  return (
    <div
      className={`space-y-2 rounded-lg p-3 text-sm ${
        test.passed ? 'bg-emerald-50 text-emerald-900 dark:bg-emerald-950/40 dark:text-emerald-100' : 'bg-red-50 text-red-900 dark:bg-red-950/40 dark:text-red-100'
      }`}
    >
      <p className="font-medium">
        {t(`wizard.switchTest.codes.${test.code}`, { defaultValue: test.code })}
        {test.simulated && ` · ${t('wizard.switchTest.simulated')}`}
      </p>
      {hint && <p>{t(`wizard.switchTest.hints.${hint}`)}</p>}
      {Array.isArray(tips) && tips.length > 0 && (
        <ul className="list-disc space-y-1 pl-5">
          {tips.map((tip) => (
            <li key={tip}>{tip}</li>
          ))}
        </ul>
      )}
      <details>
        <summary className="cursor-pointer text-xs font-medium">{t('wizard.switchTest.details')}</summary>
        <ul className="mt-1 space-y-0.5 text-xs">
          {test.networks.map((n) => (
            <li key={`${n.commId}-${n.channel}`}>
              {t('wizard.switchTest.found')}: {n.frlg ? t('wizard.switchTest.frlg') : t('wizard.switchTest.otherGame')} ·{' '}
              {t('wizard.switchTest.channel', { channel: n.channel })} · {t('wizard.switchTest.protocol', { protocol: n.protocol })} ·{' '}
              <span className="font-mono">{n.commId}</span> · {n.participants}/{n.maxParticipants}
            </li>
          ))}
          {test.undecryptable > 0 && (
            <li>
              {t('wizard.switchTest.undecryptable', {
                count: test.undecryptable,
                channels: (test.undecryptableChannels ?? []).join(', ') || '?',
              })}
            </li>
          )}
          {unusable.length > 0 && <li>{t('wizard.switchTest.unusable', { channels: unusable.join(', ') })}</li>}
          <li>{t('wizard.switchTest.sweeps', { sweeps: test.sweeps, seconds: test.seconds })}</li>
          {test.error && <li className="font-mono opacity-80">{test.error}</li>}
        </ul>
      </details>
      <p className="text-xs opacity-70">{new Date(test.at).toLocaleString(i18n.language)}</p>
    </div>
  )
}
