import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '../../components/ui'
import { api, ApiError } from '../../lib/api'
import type { SetupStatus, WorkerInfo } from '../../lib/types'

/**
 * AppImage: o componente de rede vem dentro do app, mas precisa ser copiado para uma pasta do sistema
 * (/usr/local/lib/traderock) antes do primeiro uso. Sempre com confirmação; a senha é pedida pelo
 * sistema, nunca pelo TradeRock.
 */
export function AppImageWorkerInstall({ status, onStatus }: { status: SetupStatus; onStatus: (s: SetupStatus) => void }) {
  const { t } = useTranslation()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const code = status.checks.find((c) => c.id === 'worker')?.code
  if (code !== 'worker_appimage_missing' && code !== 'worker_appimage_outdated') return null
  const outdated = code === 'worker_appimage_outdated'

  async function install() {
    setBusy(true)
    setError(null)
    try {
      onStatus(await api.post<SetupStatus>('/api/worker/install'))
    } catch (e) {
      setError(e instanceof ApiError && e.code ? e.code : 'unknown')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-2 rounded-lg bg-sky-50 p-3 text-sm dark:bg-sky-950/40">
      <p className="font-medium">{outdated ? t('workerInstall.outdatedTitle') : t('workerInstall.title')}</p>
      <p>{t('workerInstall.why')}</p>
      <ul className="list-disc space-y-1 pl-5">
        <li>
          {t('workerInstall.where')} <code>/usr/local/lib/traderock</code>
        </li>
        <li>{t('workerInstall.password')}</li>
        <li>{t('workerInstall.remove')}</li>
      </ul>
      <Button onClick={() => void install()} disabled={busy}>
        {busy ? t('workerInstall.installing') : outdated ? t('workerInstall.update') : t('workerInstall.install')}
      </Button>
      {error && (
        <p role="alert" className="text-red-800 dark:text-red-200">
          {t(`failures.${error}`, { defaultValue: t('failures.unknown') })}
        </p>
      )}
    </div>
  )
}

/** Configurações: estado do componente de rede do AppImage, com atualizar e remover. */
export function AppImageWorkerSettings() {
  const { t } = useTranslation()
  const [info, setInfo] = useState<WorkerInfo | null>(null)
  const [busy, setBusy] = useState(false)
  const [confirmRemove, setConfirmRemove] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = () => void api.get<WorkerInfo>('/api/worker').then(setInfo).catch(() => undefined)
  useEffect(load, [])

  if (!info || (!info.bundled.available && info.install?.flavor !== 'appimage')) return null

  async function run(path: string) {
    setBusy(true)
    setError(null)
    try {
      await api.post(path)
      setConfirmRemove(false)
    } catch (e) {
      setError(e instanceof ApiError && e.code ? e.code : 'unknown')
    } finally {
      setBusy(false)
      load()
    }
  }

  const b = info.bundled
  const state = !b.installed ? 'missing' : b.upToDate || !b.available ? 'ok' : 'outdated'
  return (
    <div className="space-y-2">
      <h3 className="text-sm font-semibold">{t('workerInstall.settingsTitle')}</h3>
      <p className="text-sm">{t(`workerInstall.state.${state}`)}</p>
      <div className="flex flex-wrap gap-2">
        {b.available && state !== 'ok' && (
          <Button variant="secondary" disabled={busy} onClick={() => void run('/api/worker/install')}>
            {state === 'missing' ? t('workerInstall.install') : t('workerInstall.update')}
          </Button>
        )}
        {b.installed &&
          (confirmRemove ? (
            <>
              <Button variant="danger" disabled={busy} onClick={() => void run('/api/worker/uninstall')}>
                {t('workerInstall.confirmRemove')}
              </Button>
              <Button variant="ghost" onClick={() => setConfirmRemove(false)}>
                {t('common.cancel')}
              </Button>
            </>
          ) : (
            <Button variant="secondary" disabled={busy} onClick={() => setConfirmRemove(true)}>
              {t('workerInstall.removeButton')}
            </Button>
          ))}
      </div>
      {confirmRemove && <p className="text-xs text-slate-500">{t('workerInstall.removeHelp')}</p>}
      {error && (
        <p role="alert" className="text-sm text-red-800 dark:text-red-200">
          {t(`failures.${error}`, { defaultValue: t('failures.unknown') })}
        </p>
      )}
    </div>
  )
}
