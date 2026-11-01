import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '../../components/ui'
import { api, ApiError } from '../../lib/api'
import type { Esp32FlashJob, SetupStatus } from '../../lib/types'

/**
 * Etapa 4 com o ESP32: firmware. O LDN_HELLO (rápido, sem reiniciar a placa) roda sozinho quando a
 * porta muda; identificar, conferir e gravar usam o esptool e reiniciam a placa, então ficam em botões.
 */
export function Esp32Actions({ status, onStatus }: { status: SetupStatus; onStatus: (s: SetupStatus) => void }) {
  const { t } = useTranslation()
  const { state } = status
  const port = state.serialPort
  const [busy, setBusy] = useState<'probe' | 'identify' | 'verify' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [detail, setDetail] = useState<string | null>(null)
  const [confirmFlash, setConfirmFlash] = useState(false)
  const [job, setJob] = useState<Esp32FlashJob | null>(null)
  const probed = useRef<string | null>(null)

  const probe = state.esp32Probe?.port === port ? state.esp32Probe : null
  const chip = state.esp32Chip?.port === port ? state.esp32Chip : null
  const flashing = job !== null && !job.finished
  const idle = busy === null && !flashing

  async function run(kind: 'probe' | 'identify' | 'verify') {
    setBusy(kind)
    setError(null)
    setDetail(null)
    setJob(null) // o resultado da gravação anterior sai junto com os erros antigos
    try {
      const res = await api.post<{ status: SetupStatus }>(`/api/setup/esp32/${kind}`)
      onStatus(res.status)
    } catch (e) {
      setError(e instanceof ApiError && e.code ? e.code : 'unknown')
      setDetail(e instanceof ApiError ? e.detail : null)
    } finally {
      setBusy(null)
    }
  }

  // Firmware respondendo: os erros de antes (de outra ação ou de outra tentativa) não valem mais.
  useEffect(() => {
    if (!probe?.ok) return
    setError(null)
    setDetail(null)
  }, [probe?.ok, probe?.at])

  // Conferência automática (só LDN_HELLO) ao escolher uma porta ainda não conferida.
  useEffect(() => {
    if (!port || probe || probed.current === port) return
    probed.current = port
    void run('probe')
  }, [port]) // só quando a porta muda

  async function flash() {
    setConfirmFlash(false)
    setError(null)
    setDetail(null)
    try {
      setJob(await api.post<Esp32FlashJob>('/api/setup/esp32/flash'))
    } catch (e) {
      setError(e instanceof ApiError && e.code ? e.code : 'unknown')
      setDetail(e instanceof ApiError ? e.detail : null)
    }
  }

  // Acompanha a gravação até terminar.
  useEffect(() => {
    if (!flashing) return
    const timer = setInterval(async () => {
      try {
        const res = await api.get<{ job: Esp32FlashJob; status: SetupStatus | null }>('/api/setup/esp32/flash')
        setJob(res.job)
        if (res.job.finished && res.status) onStatus(res.status)
        if (res.job.stage === 'error') {
          setError(res.job.errorCode ?? 'unknown')
          setDetail(res.job.errorMessage)
        }
      } catch {
        // tenta de novo no próximo intervalo
      }
    }, 500)
    return () => clearInterval(timer)
  }, [flashing, onStatus])

  if (!port) return null
  const percent = job && job.total > 0 ? Math.round((job.done / job.total) * 100) : null

  return (
    <div className="space-y-3 rounded-xl border border-line p-4">
      <h3 className="font-semibold">{t('wizard.esp32fw.title')}</h3>
      <p className="text-sm">{t('wizard.esp32fw.help')}</p>

      <div className="flex flex-wrap gap-2">
        <Button variant="secondary" onClick={() => void run('probe')} disabled={!idle}>
          {busy === 'probe' ? t('wizard.esp32fw.working') : t('wizard.esp32fw.probe')}
        </Button>
        <Button variant="secondary" onClick={() => void run('identify')} disabled={!idle}>
          {busy === 'identify' ? t('wizard.esp32fw.working') : t('wizard.esp32fw.identify')}
        </Button>
        <Button variant="secondary" onClick={() => void run('verify')} disabled={!idle}>
          {busy === 'verify' ? t('wizard.esp32fw.working') : t('wizard.esp32fw.verify')}
        </Button>
        <Button onClick={() => setConfirmFlash(true)} disabled={!idle}>
          {t('wizard.esp32fw.flash')}
        </Button>
      </div>
      <p className="text-xs text-muted">{t('wizard.esp32fw.identifyHelp')}</p>

      {chip && (
        <p className="text-sm">
          <span className="font-mono">{chip.description}</span> · {chip.flashSize ?? '?'}
          {chip.simulated && ` · ${t('wizard.esp32fw.simulated')}`}
        </p>
      )}
      {probe && !probe.ok && !flashing && <p className="text-sm text-warn">{t(`wizard.checks.esp32_probe_${probe.code}`)}</p>}

      {confirmFlash && (
        <div className="space-y-2 rounded-lg bg-warn-soft p-3 text-sm">
          <p className="font-medium">{t('wizard.esp32fw.flashConfirmTitle')}</p>
          <p>{t('wizard.esp32fw.flashConfirmText')}</p>
          <div className="flex gap-2">
            <Button onClick={() => void flash()}>{t('wizard.esp32fw.flashConfirm')}</Button>
            <Button variant="ghost" onClick={() => setConfirmFlash(false)}>
              {t('common.cancel')}
            </Button>
          </div>
        </div>
      )}

      {job && (
        <div className="space-y-1 text-sm" aria-live="polite">
          <p className={job.stage === 'error' ? 'text-bad' : job.stage === 'done' ? 'text-ok' : ''}>
            {t(`wizard.esp32fw.stages.${job.stage}`)}
            {percent !== null && job.stage === 'write' && ` ${percent}%`}
          </p>
          {flashing && (
            <div className="h-1.5 overflow-hidden rounded-full bg-surface2" aria-hidden>
              <div className="h-full bg-accent transition-all" style={{ width: `${percent ?? 5}%` }} />
            </div>
          )}
        </div>
      )}

      {error && (
        <p role="alert" className="rounded-lg bg-bad-soft p-3 text-sm text-bad">
          {t(`failures.${error}`, { defaultValue: t('failures.unknown') })}
          {detail && <code className="mt-1 block break-words text-xs">{detail}</code>}
        </p>
      )}

      <details open={error === 'boot_mode_required'} className="text-sm">
        <summary className="cursor-pointer font-medium">{t('wizard.esp32fw.bootTitle')}</summary>
        <div className="mt-2 flex flex-wrap items-start gap-4">
          <BootButtonsIllustration />
          <ol className="list-decimal space-y-1 pl-5">
            {(t('wizard.esp32fw.bootSteps', { returnObjects: true }) as string[]).map((step) => (
              <li key={step}>{step}</li>
            ))}
          </ol>
        </div>
      </details>
    </div>
  )
}

/** Placa com os dois botões ao lado do conector USB: BOOT (IO0) e RESET (EN). */
function BootButtonsIllustration() {
  return (
    <svg viewBox="0 0 220 120" role="img" aria-label="BOOT / RESET" className="w-56 max-w-full">
      <text x="14" y="12" textAnchor="middle" fontSize="9" className="fill-slate-500">USB</text>
      <rect x="16" y="20" width="200" height="80" rx="6" className="fill-slate-800 stroke-slate-600" strokeWidth="2" />
      <rect x="2" y="47" width="24" height="26" rx="3" className="fill-slate-400" />
      <rect x="130" y="35" width="70" height="50" rx="3" className="fill-slate-300" />
      <text x="165" y="64" textAnchor="middle" fontSize="10" fontWeight="700" className="fill-slate-800">ESP32</text>
      <rect x="38" y="28" width="18" height="16" rx="2" className="fill-slate-500" />
      <circle cx="47" cy="36" r="5" className="fill-amber-400" />
      <text x="62" y="40" fontSize="10" fontWeight="700" className="fill-amber-300">BOOT (IO0)</text>
      <rect x="38" y="76" width="18" height="16" rx="2" className="fill-slate-500" />
      <circle cx="47" cy="84" r="5" className="fill-sky-400" />
      <text x="62" y="88" fontSize="10" fontWeight="700" className="fill-sky-300">RESET (EN)</text>
    </svg>
  )
}
