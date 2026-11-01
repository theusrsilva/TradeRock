import ReactMarkdown from 'react-markdown'
import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Button, Callout, Toggle } from '../components/ui'
import { api, ApiError } from '../lib/api'
import { useAuth } from '../lib/auth'

export type InstallKind = 'none' | 'windows_installer' | 'app_image' | 'deb'

export interface UpdateStatus {
  current: string
  kind: InstallKind
  autoCheck: boolean
  checkedAt: string | null
  latest: { version: string; tag: string; url: string; notes: string; publishedAt: string | null } | null
  available: boolean
  stage: 'idle' | 'checking' | 'downloading' | 'verifying' | 'installing' | 'restarting' | 'error'
  done: number
  total: number
  errorCode: string | null
  errorMessage: string | null
}

const working = (s: UpdateStatus | null) => !!s && ['downloading', 'verifying', 'installing', 'restarting'].includes(s.stage)

/** Status da atualização (D29), só na janela do PC; acompanha a instalação até o app fechar. */
export function useUpdate() {
  const { me } = useAuth()
  const local = me?.local !== false
  const [status, setStatus] = useState<UpdateStatus | null>(null)

  const reload = useCallback(async () => {
    if (!local) return
    try {
      setStatus(await api.get<UpdateStatus>('/api/update'))
    } catch {
      // ignora
    }
  }, [local])

  useEffect(() => {
    void reload()
  }, [reload])

  // A procura automática roda uns segundos depois de abrir; e a instalação muda de etapa sozinha.
  useEffect(() => {
    if (!local) return
    const timer = setInterval(() => void reload(), working(status) ? 700 : 20000)
    return () => clearInterval(timer)
  }, [local, reload, status])

  return { status, setStatus, reload }
}

/** Aviso no Início quando há versão nova. */
export function UpdateNotice() {
  const { t } = useTranslation()
  const { status } = useUpdate()
  if (!status?.available || !status.latest) return null
  return (
    <Callout tone="sky" icon="system_update">
      {t('update.notice', { version: status.latest.version })}{' '}
      <Link to="/sobre" className="font-semibold underline">
        {t('update.see')}
      </Link>
    </Callout>
  )
}

/** Cartão "Atualizações" da tela Sobre. */
export function UpdateCard() {
  const { t, i18n } = useTranslation()
  const { status, setStatus } = useUpdate()
  const [checking, setChecking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirm, setConfirm] = useState(false)

  if (!status) return null
  const busy = working(status)
  const date = (at: string) => new Date(at).toLocaleString(i18n.language, { dateStyle: 'short', timeStyle: 'short' })
  const percent = status.total > 0 ? Math.round((status.done / status.total) * 100) : null

  async function check() {
    setChecking(true)
    setError(null)
    try {
      setStatus(await api.post<UpdateStatus>('/api/update/check'))
    } catch (e) {
      setError(e instanceof ApiError && e.code ? e.code : 'update_check_failed')
    } finally {
      setChecking(false)
    }
  }

  async function install() {
    setConfirm(false)
    setError(null)
    try {
      setStatus(await api.post<UpdateStatus>('/api/update/install'))
    } catch (e) {
      setError(e instanceof ApiError && e.code ? e.code : 'update_failed')
    }
  }

  async function setAuto(on: boolean) {
    setStatus(await api.put<UpdateStatus>('/api/update/auto', { on }))
  }

  const shownError = error ?? (status.stage === 'error' ? status.errorCode : null)

  return (
    <div className="flex flex-col gap-3 rounded-xl border border-line bg-surface p-[22px]">
      <div className="flex flex-wrap items-center gap-3">
        <div className="min-w-[200px] flex-1">
          <div className="text-base font-extrabold">{t('update.title')}</div>
          <div className="text-[13px] text-muted">
            {status.checkedAt ? t('update.checkedAt', { date: date(status.checkedAt) }) : t('update.neverChecked')}
          </div>
        </div>
        <Button variant="secondary" icon="refresh" disabled={checking || busy} onClick={() => void check()}>
          {checking ? t('update.checking') : t('update.check')}
        </Button>
      </div>
      <Toggle small on={status.autoCheck} onChange={(on) => void setAuto(on)} label={t('update.auto')} />
      <p className="text-[13px] text-muted">{t('update.privacy')}</p>

      {status.latest && !status.available && !busy && <p className="text-sm text-ok">{t('update.upToDate', { version: status.current })}</p>}

      {status.available && status.latest && (
        <div className="flex flex-col gap-2 rounded-lg bg-surface2 p-3.5">
          <b>{t('update.available', { version: status.latest.version, current: status.current })}</b>
          {status.latest.notes && (
            <details className="text-sm">
              <summary className="cursor-pointer font-semibold text-accent">{t('update.notes')}</summary>
              <div className="mt-2 [&_a]:text-accent [&_a]:underline [&_li]:ml-5 [&_li]:list-disc [&_p]:my-1.5">
                <ReactMarkdown>{status.latest.notes}</ReactMarkdown>
              </div>
            </details>
          )}
          {status.kind === 'none' ? (
            <p className="text-sm">
              {t('update.manual')}{' '}
              <a href={status.latest.url} target="_blank" rel="noreferrer" className="text-accent underline">
                {t('update.openRelease')}
              </a>
            </p>
          ) : busy ? (
            <div className="space-y-1 text-sm" aria-live="polite">
              <p>
                {t(`update.stages.${status.stage}`)}
                {status.stage === 'downloading' && percent !== null && ` ${percent}%`}
              </p>
              <div className="h-1.5 overflow-hidden rounded-full bg-surface" aria-hidden>
                <div className="h-full bg-accent transition-all" style={{ width: `${status.stage === 'downloading' ? (percent ?? 5) : 100}%` }} />
              </div>
            </div>
          ) : confirm ? (
            <div className="space-y-2 text-sm">
              <p>{t(`update.confirm.${status.kind}`)}</p>
              <div className="flex gap-2">
                <Button icon="system_update" onClick={() => void install()}>
                  {t('update.install')}
                </Button>
                <Button variant="ghost" onClick={() => setConfirm(false)}>
                  {t('common.cancel')}
                </Button>
              </div>
            </div>
          ) : (
            <Button className="self-start" icon="system_update" onClick={() => setConfirm(true)}>
              {t('update.installVersion', { version: status.latest.version })}
            </Button>
          )}
        </div>
      )}

      {shownError && (
        <p role="alert" className="rounded-lg bg-bad-soft p-3 text-sm text-bad">
          {t(`update.errors.${shownError}`, { defaultValue: t('update.errors.update_failed') })}
          {status.stage === 'error' && !error && status.errorMessage && <code className="mt-1 block break-words text-xs">{status.errorMessage}</code>}
        </p>
      )}
    </div>
  )
}
