import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Icon, Logo, inputClass } from '../components/ui'
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
    <div className="flex min-h-full items-center justify-center bg-bg p-5 text-ink">
      <div className="flex w-full max-w-[380px] flex-col gap-4">
        <div className="flex flex-col items-center gap-2.5 text-center">
          <Logo size={56} />
          <h1 className="font-display text-[26px] font-bold">{t('app.name')}</h1>
          <p className="text-sm text-muted">{t('login.intro')}</p>
        </div>
        <form className="flex flex-col gap-3 rounded-xl border border-line bg-surface p-5" onSubmit={(e) => void submit(e)}>
          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-bold">{t('login.username')}</span>
            <input className={`${inputClass} h-12! text-base!`} autoComplete="username" autoCapitalize="none" value={username} onChange={(e) => setUsername(e.target.value)} />
          </label>
          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-bold">{t('login.password')}</span>
            <input type="password" className={`${inputClass} h-12! text-base!`} autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
          </label>
          {error && (
            <p role="alert" className="text-[13px] text-bad">
              {t(`login.errors.${error}`, { defaultValue: t('common.error') })}
            </p>
          )}
          <Button type="submit" size="lg" className="h-[50px] w-full text-base font-extrabold" disabled={busy || !username || !password}>
            {busy ? t('common.loading') : t('login.submit')}
          </Button>
        </form>
        <p className="text-center text-[12.5px] text-muted">{t('login.noAccount')}</p>
        <p className="flex items-center justify-center gap-2 text-[12.5px] text-muted">
          <Icon name="home" className="text-base" />
          {t('ui.login.homeOnly')}
        </p>
      </div>
    </div>
  )
}
