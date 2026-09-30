import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Card, Field, inputClass } from '../components/ui'
import { ApiError, api } from '../lib/api'

/** Entrada pela rede (modo servidor). A janela do próprio PC nunca vê esta tela. */
export default function LoginPage({ onLogin }: { onLogin: () => Promise<void> }) {
  const { t } = useTranslation()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.post('/api/auth/login', { username, password })
      await onLogin()
    } catch (err) {
      setError(err instanceof ApiError && err.code ? err.code : 'unknown')
      setPassword('')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex min-h-full items-center justify-center bg-slate-50 p-4 text-slate-900 dark:bg-slate-950 dark:text-slate-100">
      <Card className="w-full max-w-sm space-y-4">
        <div className="flex items-center gap-2">
          <img src="/icon.svg" alt="" className="h-8 w-8" />
          <h1 className="text-lg font-semibold">{t('login.title')}</h1>
        </div>
        <p className="text-sm text-slate-600 dark:text-slate-300">{t('login.intro')}</p>
        <form className="space-y-3" onSubmit={(e) => void submit(e)}>
          <Field label={t('login.username')}>
            <input className={inputClass} autoComplete="username" autoCapitalize="none" value={username} onChange={(e) => setUsername(e.target.value)} />
          </Field>
          <Field label={t('login.password')}>
            <input type="password" className={inputClass} autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
          </Field>
          {error && (
            <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-200">
              {t(`login.errors.${error}`, { defaultValue: t('common.error') })}
            </p>
          )}
          <Button type="submit" className="w-full" disabled={busy || !username || !password}>
            {busy ? t('common.loading') : t('login.submit')}
          </Button>
        </form>
        <p className="text-xs text-slate-500">{t('login.noAccount')}</p>
      </Card>
    </div>
  )
}
