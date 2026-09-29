import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Card } from '../components/ui'
import { api } from '../lib/api'
import type { Health, TradeSession } from '../lib/types'

type HealthState = { kind: 'loading' } | { kind: 'ok'; data: Health } | { kind: 'error' }

const linkButton = 'inline-flex items-center rounded-lg px-4 py-2 text-sm font-medium'

export default function HomePage() {
  const { t } = useTranslation()
  const [health, setHealth] = useState<HealthState>({ kind: 'loading' })
  const [session, setSession] = useState<TradeSession | null>(null)

  useEffect(() => {
    api
      .get<Health>('/api/health')
      .then((data) => setHealth({ kind: 'ok', data }))
      .catch(() => setHealth({ kind: 'error' }))
    api
      .get<TradeSession | undefined>('/api/trades/current')
      .then((s) => setSession(s ?? null))
      .catch(() => undefined)
  }, [])

  return (
    <div className="grid gap-4 md:grid-cols-[2fr_1fr]">
      <Card className="space-y-4">
        <h1 className="text-xl font-semibold">{t('home.title')}</h1>
        <p className="text-slate-600 dark:text-slate-300">{t('home.intro')}</p>
        {session?.status === 'running' && (
          <p className="rounded-lg bg-sky-50 p-3 text-sm text-sky-900 dark:bg-sky-950/40 dark:text-sky-200">
            {t('home.running')}{' '}
            <Link to="/trocar" className="font-semibold underline">
              {t('home.follow')}
            </Link>
          </p>
        )}
        <div className="flex flex-wrap gap-2">
          <Link to="/trocar" className={`${linkButton} bg-sky-600 text-white hover:bg-sky-700`}>
            {t('home.startTrade')}
          </Link>
          <Link
            to="/pokemon"
            className={`${linkButton} border border-slate-300 hover:bg-slate-50 dark:border-slate-600 dark:hover:bg-slate-800`}
          >
            {t('home.seePokemon')}
          </Link>
        </div>
      </Card>

      <Card>
        <h2 className="mb-3 font-semibold">{t('status.title')}</h2>
        {health.kind === 'loading' && <p className="text-slate-500">{t('status.checking')}</p>}
        {health.kind === 'error' && <p className="text-red-600 dark:text-red-400">✕ {t('status.error')}</p>}
        {health.kind === 'ok' && (
          <>
            <p className="mb-3 text-emerald-700 dark:text-emerald-400">✓ {t('status.ok')}</p>
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
              <dt className="text-slate-500">{t('status.version')}</dt>
              <dd className="break-all">{health.data.version.split('+')[0]}</dd>
              <dt className="text-slate-500">{t('status.system')}</dt>
              <dd className="capitalize">{health.data.os}</dd>
              <dt className="text-slate-500">{t('status.environment')}</dt>
              <dd>{health.data.environment}</dd>
            </dl>
          </>
        )}
      </Card>
    </div>
  )
}
