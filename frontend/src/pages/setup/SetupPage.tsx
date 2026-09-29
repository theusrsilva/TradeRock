import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'
import { ContentFile } from '../../components/Markdown'
import { SwitchReady } from '../../components/illustrations/SwitchReady'
import { Badge, Button, Card, inputClass } from '../../components/ui'
import { api } from '../../lib/api'
import { downloadBlob } from '../../lib/appInfo'
import type { HardwareCatalog, SetupStatus } from '../../lib/types'
import { CatalogNotice, CheckList, CompatBadge, Esp32Illustration, Esp32Table, WifiTable } from './parts'
import { WifiActions } from './WifiActions'

const TOTAL = 7

type Patch = Partial<Pick<SetupStatus['state'], 'radio' | 'phy' | 'serialPort' | 'esp32Board' | 'esp32ModuleConfirmed' | 'keysSkipped' | 'completed'>>

export default function SetupPage() {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const step = Math.min(TOTAL, Math.max(1, Number(params.get('etapa')) || 1))
  const [status, setStatus] = useState<SetupStatus | null>(null)
  const [catalog, setCatalog] = useState<HardwareCatalog | null>(null)
  const [scenarios, setScenarios] = useState<{ scenarios: string[]; current: string | null } | null>(null)

  const load = useCallback(() => {
    api.get<SetupStatus>('/api/setup').then(setStatus).catch(() => undefined)
  }, [])

  useEffect(() => {
    load()
    api.get<HardwareCatalog>('/api/setup/catalog').then(setCatalog).catch(() => undefined)
    // Só existe em desenvolvimento (404 no app final).
    api.get<{ scenarios: string[]; current: string | null }>('/api/setup/scenarios').then(setScenarios).catch(() => undefined)
  }, [load])

  const patch = async (p: Patch) => setStatus(await api.put<SetupStatus>('/api/setup/state', p))
  const go = (n: number) => setParams({ etapa: String(n) })

  // Concluir com a placa Wi-Fi pronta: as próximas trocas já usam o motor da placa.
  async function finish() {
    await patch({ completed: true })
    if (status?.readyToTrade && status.state.radio === 'wifi' && !status.system.simulatedScenario)
      await api.put('/api/engines/selected', { id: 'python-ldn' }).catch(() => undefined)
  }

  if (!status || !catalog) return <p className="text-slate-500">{t('wizard.detecting')}</p>

  const stepChecks = (n: number) => status.checks.filter((c) => c.step === n)
  const steps = t('wizard.steps', { returnObjects: true }) as string[]

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-semibold">{t('wizard.title')}</h1>
        {scenarios && (
          <label className="flex items-center gap-2 text-xs">
            <Badge tone="amber">{t('wizard.simulation')}</Badge>
            <select
              className={`${inputClass} w-auto! py-1`}
              value={scenarios.current ?? ''}
              onChange={async (e) => {
                const scenario = e.target.value || null
                setStatus(await api.put<SetupStatus>('/api/setup/simulation', { scenario }))
                setScenarios({ ...scenarios, current: scenario })
              }}
            >
              <option value="">{t('wizard.simulationNone')}</option>
              {scenarios.scenarios.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>

      {/* Barra de progresso com as etapas */}
      <div>
        <div className="mb-2 h-2 overflow-hidden rounded-full bg-slate-200 dark:bg-slate-800">
          <div className="h-full bg-sky-600 transition-all" style={{ width: `${(step / TOTAL) * 100}%` }} />
        </div>
        <ol className="flex flex-wrap gap-1 text-xs">
          {steps.map((name, i) => {
            const n = i + 1
            const bad = stepChecks(n).some((c) => c.status === 'error')
            return (
              <li key={n}>
                <button
                  type="button"
                  onClick={() => go(n)}
                  className={`rounded-full px-2.5 py-1 ${
                    n === step ? 'bg-sky-600 text-white' : bad ? 'bg-red-100 text-red-800 dark:bg-red-900/50 dark:text-red-200' : 'bg-slate-200 text-slate-700 dark:bg-slate-800 dark:text-slate-300'
                  }`}
                >
                  {n}. {name}
                </button>
              </li>
            )
          })}
        </ol>
        <p className="mt-1 text-xs text-slate-500">{t('wizard.stepOf', { n: step, total: TOTAL })}</p>
      </div>

      <Card className="space-y-4">
        {step === 1 && <WelcomeStep status={status} />}
        {step === 2 && <KeysStep status={status} onChange={load} onSkip={() => void patch({ keysSkipped: true }).then(() => go(3))} />}
        {step === 3 && <RadioStep status={status} catalog={catalog} onChoose={(radio) => void patch({ radio })} />}
        {step === 4 && <PrepareStep status={status} catalog={catalog} onPatch={(p) => void patch(p)} onRefresh={load} onStatus={setStatus} />}
        {step === 5 && (
          <>
            <SwitchReady labels={[t('trade.reminder.illustration.center'), t('trade.reminder.illustration.counter'), t('trade.reminder.illustration.leader')]} />
            <ContentFile name="wizard/05-jogo" />
          </>
        )}
        {step === 6 && (
          <>
            <ContentFile name="wizard/06-teste" />
            <CheckList checks={stepChecks(6)} />
          </>
        )}
        {step === 7 && <SummaryStep status={status} onGo={go} onRestart={() => void api.post<SetupStatus>('/api/setup/reset').then((s) => { setStatus(s); go(1) })} />}
      </Card>

      <div className="flex justify-between">
        <Button variant="secondary" disabled={step === 1} onClick={() => go(step - 1)}>
          {t('wizard.back')}
        </Button>
        {step < TOTAL ? (
          <Button onClick={() => go(step + 1)}>{t('wizard.next')}</Button>
        ) : (
          <Button onClick={() => void finish()}>{t('wizard.finish')}</Button>
        )}
      </div>
    </div>
  )
}

function WelcomeStep({ status }: { status: SetupStatus }) {
  const { t } = useTranslation()
  const { advice, system } = status
  const best = advice.bestCard
  const model = best?.matches[0]?.model ?? best?.card.productName ?? best?.card.module ?? best?.card.driver ?? '?'
  const tone = advice.situation === 'wifi_compatible' ? 'emerald' : advice.situation === 'wifi_test_first' ? 'amber' : 'sky'

  return (
    <div className="space-y-4">
      <ContentFile name="wizard/01-boas-vindas" />
      <div className={`rounded-lg p-3 text-sm ${tone === 'emerald' ? 'bg-emerald-50 text-emerald-900 dark:bg-emerald-950/40 dark:text-emerald-200' : tone === 'amber' ? 'bg-amber-50 text-amber-900 dark:bg-amber-950/40 dark:text-amber-200' : 'bg-sky-50 text-sky-900 dark:bg-sky-950/40 dark:text-sky-200'}`}>
        {t(`wizard.preview.${advice.situation}`, { model, vm: system.virtualMachine ?? '', why: t(`wizard.why.${best?.status === 'unstable' ? 'unstable' : 'untested'}`) })}
      </div>
      <div>
        <h2 className="mb-2 font-semibold">{t('wizard.needTitle')}</h2>
        <ContentFile name="wizard/01-precisa-todos" />
        <ContentFile name={system.os === 'windows' ? 'wizard/01-precisa-windows' : 'wizard/01-precisa-linux'} />
      </div>
      <p className="text-sm text-slate-500">{t('wizard.estimated')}</p>
    </div>
  )
}

function KeysStep({ status, onChange, onSkip }: { status: SetupStatus; onChange: () => void; onSkip: () => void }) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const [problem, setProblem] = useState<SetupStatus['keys'] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const keys = status.keys

  async function upload(file: File) {
    setProblem(null)
    setError(null)
    const form = new FormData()
    form.append('file', file, 'prod.keys')
    // Um envio só. Em caso de formato inválido, a resposta traz a validação (nomes das chaves, nunca valores).
    const response = await fetch('/api/setup/keys', { method: 'POST', body: form, credentials: 'same-origin' })
    if (response.ok) {
      onChange()
      return
    }
    try {
      const body = (await response.json()) as { error?: string; validation?: SetupStatus['keys'] }
      if (body.validation) setProblem(body.validation)
      else setError(body.error ?? 'error')
    } catch {
      setError('error')
    }
  }

  return (
    <div className="space-y-4">
      <ContentFile name="wizard/02-prod-keys" />
      <input ref={input} type="file" hidden onChange={(e) => { if (e.target.files?.[0]) void upload(e.target.files[0]); e.target.value = '' }} />
      {keys?.valid && <p className="rounded-lg bg-emerald-50 p-3 text-sm text-emerald-900 dark:bg-emerald-950/40 dark:text-emerald-200">✓ {t('wizard.keysOk', { protocols: keys.protocols.join(', ') })}</p>}
      {(problem || (keys && !keys.valid)) && (
        <div className="rounded-lg bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-300">
          <p className="font-medium">{t('wizard.keysInvalidTitle')}</p>
          {(problem ?? keys)!.missingRequired.length > 0 && <p>• {t('wizard.keysMissing', { keys: (problem ?? keys)!.missingRequired.join(', ') })}</p>}
          {(problem ?? keys)!.invalidLines.length > 0 && <p>• {t('wizard.keysLines', { lines: (problem ?? keys)!.invalidLines.join(', ') })}</p>}
        </div>
      )}
      {error && <p className="text-sm text-red-700 dark:text-red-400">{t('common.error')}</p>}
      <div className="flex flex-wrap gap-2">
        <Button onClick={() => input.current?.click()}>{keys ? t('wizard.keysReplace') : t('wizard.keysUpload')}</Button>
        {keys && (
          <Button variant="ghost" onClick={() => void api.delete('/api/setup/keys').then(onChange)}>
            {t('wizard.keysRemove')}
          </Button>
        )}
        {!keys?.valid && (
          <Button variant="secondary" onClick={onSkip}>
            {t('wizard.keysSkip')}
          </Button>
        )}
      </div>
    </div>
  )
}

function RadioStep({ status, catalog, onChoose }: { status: SetupStatus; catalog: HardwareCatalog; onChoose: (radio: 'wifi' | 'esp32') => void }) {
  const { t } = useTranslation()
  const { advice, state, system } = status
  const windows = system.os === 'windows'
  const detectedDrivers = advice.cards.flatMap((c) => [c.card.module, c.card.driver]).filter((d): d is string => !!d)
  // No Linux, a placa Wi-Fi aparece primeiro; no Windows, só o ESP32.
  const options = windows ? advice.options.filter((o) => o.engine === 'esp32') : advice.options

  return (
    <div className="space-y-4">
      <ContentFile name={windows ? 'wizard/03-radio-windows' : 'wizard/03-radio-linux'} />
      <div className="grid gap-3 md:grid-cols-2">
        {options.map((o) => (
          <div
            key={o.engine}
            className={`space-y-2 rounded-xl border p-4 ${o.recommended ? 'border-sky-500' : 'border-slate-200 dark:border-slate-700'} ${!o.available ? 'opacity-60' : ''}`}
          >
            <div className="flex flex-wrap gap-2">
              {o.recommended && <Badge tone="sky">{t('wizard.recommended')}</Badge>}
              {o.engine === 'wifi' && advice.situation === 'wifi_compatible' && <Badge tone="emerald">{t('wizard.noBuy')}</Badge>}
              {!o.available && <Badge tone="slate">{t('wizard.unavailable')}</Badge>}
            </div>
            <ContentFile name={o.engine === 'wifi' ? 'wizard/03-opcao-wifi' : 'wizard/03-opcao-esp32'} />
            {o.reasonCode && <p className="text-sm text-slate-600 dark:text-slate-300">{t(`wizard.reasons.${o.reasonCode}`)}</p>}
            {o.engine === 'wifi' && advice.bestCard && (
              <p className="flex items-center gap-2 text-sm">
                {t('wizard.yourCard')}: <strong>{advice.bestCard.matches[0]?.model ?? advice.bestCard.card.module}</strong>
                <CompatBadge status={advice.bestCard.status} />
              </p>
            )}
            <Button variant={state.radio === o.engine ? 'primary' : 'secondary'} disabled={!o.available} onClick={() => onChoose(o.engine)}>
              {state.radio === o.engine ? `✓ ${t('wizard.chosen')}` : t('wizard.choose')}
            </Button>
          </div>
        ))}
      </div>

      {advice.suggestedUsbAdapters.length > 0 && !windows && (
        <div className="rounded-lg bg-slate-50 p-3 text-sm dark:bg-slate-800/60">
          <p className="font-medium">{t('wizard.usbAdapterTitle')}</p>
          <p>{advice.suggestedUsbAdapters.map((a) => a.model).join(', ')}</p>
          <p className="mt-1 text-xs text-slate-600 dark:text-slate-300">{t('wizard.usbAdapterPros')}</p>
          <p className="text-xs text-slate-600 dark:text-slate-300">{t('wizard.esp32Pros')}</p>
        </div>
      )}

      {!windows && (
        <details open={advice.cards.length > 0}>
          <summary className="cursor-pointer font-medium">{t('wizard.catalogWifi')}</summary>
          <div className="mt-2 space-y-2">
            <CatalogNotice catalog={catalog} />
            <WifiTable entries={catalog.wifi} highlight={detectedDrivers} />
          </div>
        </details>
      )}
      <details open={windows}>
        <summary className="cursor-pointer font-medium">{t('wizard.catalogEsp32')}</summary>
        <div className="mt-2 space-y-3">
          <ContentFile name="wizard/03-guia-esp32" />
          <Esp32Illustration />
          <CatalogNotice catalog={catalog} />
          <Esp32Table entries={catalog.esp32} highlight={state.esp32Board} />
        </div>
      </details>
    </div>
  )
}

function PrepareStep({
  status,
  catalog,
  onPatch,
  onRefresh,
  onStatus,
}: {
  status: SetupStatus
  catalog: HardwareCatalog
  onPatch: (p: Patch) => void
  onRefresh: () => void
  onStatus: (s: SetupStatus) => void
}) {
  const { t } = useTranslation()
  const { state, advice, system } = status
  const checks = status.checks.filter((c) => c.step === 4 || c.step === 3)
  const board = catalog.esp32.find((b) => b.id === state.esp32Board)

  if (!state.radio) {
    return <CheckList checks={status.checks.filter((c) => c.id === 'radio')} />
  }

  return (
    <div className="space-y-4">
      {state.radio === 'wifi' ? (
        <>
          <ContentFile name="wizard/04-placa" />
          {advice.cards.length > 0 && (
            <fieldset className="space-y-1">
              <legend className="font-medium">{t('wizard.choosePhy')}</legend>
              {advice.cards.map((c) => (
                <label key={c.card.phy} className="flex flex-wrap items-center gap-2 text-sm">
                  <input type="radio" name="phy" checked={state.phy === c.card.phy} onChange={() => onPatch({ phy: c.card.phy })} />
                  <span className="font-mono">{c.card.phy}</span> · {c.matches[0]?.model ?? c.card.productName ?? c.card.module} ·{' '}
                  <span className="font-mono text-xs">{c.card.module ?? c.card.driver}</span> · {c.card.bus.toUpperCase()}
                  <CompatBadge status={c.status} />
                </label>
              ))}
            </fieldset>
          )}
          <WifiActions status={status} onStatus={onStatus} onUseEsp32={() => onPatch({ radio: 'esp32' })} />
        </>
      ) : (
        <>
          <ContentFile name="wizard/04-esp32" />
          <label className="block text-sm">
            <span className="font-medium">{t('wizard.chooseBoard')}</span>
            <select className={inputClass} value={state.esp32Board ?? ''} onChange={(e) => onPatch({ esp32Board: e.target.value })}>
              <option value="" disabled>
                {t('wizard.chooseBoardPlaceholder')}
              </option>
              {catalog.esp32.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.model} — {t(`wizard.compat.${b.status}`)}
                </option>
              ))}
            </select>
          </label>
          {board?.module && board.status !== 'unsupported' && (
            <div className="space-y-2 rounded-lg bg-amber-50 p-3 text-sm dark:bg-amber-950/40">
              <Esp32Illustration />
              <p>{t('wizard.moduleHelp')}</p>
              <label className="flex items-center gap-2 font-medium">
                <input type="checkbox" checked={state.esp32ModuleConfirmed} onChange={(e) => onPatch({ esp32ModuleConfirmed: e.target.checked })} />
                {t('wizard.confirmModule')}
              </label>
            </div>
          )}
          <fieldset className="space-y-1">
            <legend className="font-medium">{t('wizard.choosePort')}</legend>
            {system.serialPorts.length === 0 && <p className="text-sm text-slate-500">{t('wizard.noPorts')}</p>}
            {system.serialPorts.map((p) => {
              const chip = catalog.usbSerial.find((u) => u.vid === p.vendorId && u.pid === p.productId)
              return (
                <label key={p.path} className="flex items-center gap-2 text-sm">
                  <input type="radio" name="port" checked={state.serialPort === p.path} onChange={() => onPatch({ serialPort: p.path })} />
                  <span className="font-mono">{p.path}</span> · {chip?.name ?? p.productName ?? `${p.vendorId}:${p.productId}`}
                </label>
              )
            })}
          </fieldset>
        </>
      )}
      <CheckList checks={checks} />
      <Button variant="secondary" onClick={onRefresh}>
        {t('wizard.refresh')}
      </Button>
    </div>
  )
}

function SummaryStep({ status, onGo, onRestart }: { status: SetupStatus; onGo: (n: number) => void; onRestart: () => void }) {
  const { t } = useTranslation()
  const steps = t('wizard.steps', { returnObjects: true }) as string[]

  async function exportReport() {
    const response = await fetch('/api/setup/report', { credentials: 'same-origin' })
    downloadBlob(await response.blob(), 'traderock-relatorio.md')
  }

  return (
    <div className="space-y-4">
      <ContentFile name="wizard/07-resumo" />
      <p className={`rounded-lg p-3 text-sm font-medium ${status.readyToTrade ? 'bg-emerald-50 text-emerald-900 dark:bg-emerald-950/40 dark:text-emerald-200' : 'bg-amber-50 text-amber-900 dark:bg-amber-950/40 dark:text-amber-200'}`}>
        {status.readyToTrade ? t('wizard.ready') : t('wizard.notReady')} {!status.readyToTrade && t('wizard.blockedTrade')}
      </p>
      {[2, 3, 4, 6].map((n) => (
        <div key={n} className="space-y-2">
          <div className="flex items-center justify-between">
            <h3 className="font-semibold">
              {n}. {steps[n - 1]}
            </h3>
            <Button variant="ghost" onClick={() => onGo(n)}>
              {t('wizard.editStep')}
            </Button>
          </div>
          <CheckList checks={status.checks.filter((c) => c.step === n)} />
        </div>
      ))}
      <div className="flex flex-wrap gap-2">
        <Button onClick={() => void exportReport()}>{t('wizard.exportReport')}</Button>
        <Button variant="secondary" onClick={() => onGo(3)}>
          {t('wizard.switchEngine')}
        </Button>
        <Link to="/trocar" className="inline-flex items-center rounded-lg px-4 py-2 text-sm text-sky-700 hover:underline dark:text-sky-400">
          {t('nav.trade')}
        </Link>
        <Button variant="ghost" onClick={onRestart}>
          {t('wizard.restart')}
        </Button>
      </div>
    </div>
  )
}
