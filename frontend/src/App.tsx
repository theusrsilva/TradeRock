import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { apiGet, type Health } from './api'
import { languages } from './i18n'
import { useTheme, type ThemeChoice } from './theme'

type HealthState = { kind: 'loading' } | { kind: 'ok'; data: Health } | { kind: 'error' }

export default function App() {
  const { t, i18n } = useTranslation()
  const [theme, setTheme] = useTheme()
  const [health, setHealth] = useState<HealthState>({ kind: 'loading' })

  useEffect(() => {
    apiGet<Health>('/api/health')
      .then((data) => setHealth({ kind: 'ok', data }))
      .catch(() => setHealth({ kind: 'error' }))
  }, [])

  return (
    <div className="min-h-full bg-slate-50 text-slate-900 dark:bg-slate-950 dark:text-slate-100">
      <header className="border-b border-slate-200 bg-white px-4 py-3 dark:border-slate-800 dark:bg-slate-900">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <img src="/icon.svg" alt="" className="h-8 w-8" />
            <div>
              <h1 className="text-lg font-semibold leading-tight">{t('app.name')}</h1>
              <p className="text-sm text-slate-500 dark:text-slate-400">{t('app.tagline')}</p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-3 text-sm">
            <label className="flex items-center gap-2">
              <span>{t('settings.theme')}</span>
              <select
                className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-800"
                value={theme}
                onChange={(e) => setTheme(e.target.value as ThemeChoice)}
              >
                <option value="system">{t('settings.themeSystem')}</option>
                <option value="light">{t('settings.themeLight')}</option>
                <option value="dark">{t('settings.themeDark')}</option>
              </select>
            </label>
            <label className="flex items-center gap-2">
              <span>{t('settings.language')}</span>
              <select
                className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-800"
                value={i18n.language}
                onChange={(e) => void i18n.changeLanguage(e.target.value)}
              >
                {languages.map((l) => (
                  <option key={l.code} value={l.code}>
                    {l.label}
                  </option>
                ))}
              </select>
            </label>
          </div>
        </div>
      </header>

      <main className="mx-auto grid max-w-5xl gap-4 px-4 py-6 md:grid-cols-2">
        <section className="rounded-xl border border-slate-200 bg-white p-5 dark:border-slate-800 dark:bg-slate-900">
          <h2 className="mb-3 font-semibold">{t('status.title')}</h2>
          {health.kind === 'loading' && <p className="text-slate-500">{t('status.checking')}</p>}
          {health.kind === 'error' && (
            <p className="flex items-center gap-2 text-red-600 dark:text-red-400">
              <span aria-hidden>✕</span> {t('status.error')}
            </p>
          )}
          {health.kind === 'ok' && (
            <>
              <p className="mb-3 flex items-center gap-2 text-emerald-700 dark:text-emerald-400">
                <span aria-hidden>✓</span> {t('status.ok')}
              </p>
              <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
                <dt className="text-slate-500">{t('status.version')}</dt>
                <dd>{health.data.version}</dd>
                <dt className="text-slate-500">{t('status.system')}</dt>
                <dd className="capitalize">{health.data.os}</dd>
                <dt className="text-slate-500">{t('status.environment')}</dt>
                <dd>{health.data.environment}</dd>
              </dl>
            </>
          )}
        </section>

        <section className="rounded-xl border border-slate-200 bg-white p-5 dark:border-slate-800 dark:bg-slate-900">
          <h2 className="mb-3 font-semibold">{t('next.title')}</h2>
          <p className="text-sm text-slate-600 dark:text-slate-300">{t('next.body')}</p>
        </section>
      </main>
    </div>
  )
}
