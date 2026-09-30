import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Badge, Button, Card, Field, inputClass, Modal } from '../../components/ui'
import { ApiError, api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { useAppInfo } from '../../lib/appInfo'
import type { Account, ServerModeStatus, UserRole } from '../../lib/types'

function errorCode(e: unknown) {
  return e instanceof ApiError && e.code ? e.code : 'unknown'
}

/** Liga/desliga o modo servidor (só na janela do PC). */
export function ServerModeCard() {
  const { t } = useTranslation()
  const { reload } = useAuth()
  const info = useAppInfo()
  const [status, setStatus] = useState<ServerModeStatus | null>(null)
  const [choice, setChoice] = useState('')
  const [port, setPort] = useState(8337)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    const s = await api.get<ServerModeStatus>('/api/server')
    setStatus(s)
    setPort(s.port)
    const usable = s.interfaces.filter((i) => !i.blocked && !i.virtual).concat(s.interfaces.filter((i) => !i.blocked && i.virtual))
    setChoice((current) => current || (usable.some((i) => i.name === s.interface) ? s.interface! : usable[0]?.name) || '')
  }, [])

  useEffect(() => {
    void load().catch(() => undefined)
  }, [load])

  async function toggle(enabled: boolean) {
    setBusy(true)
    setError(null)
    try {
      setStatus(await api.put<ServerModeStatus>('/api/server', { enabled, interface: choice, port }))
      await reload()
    } catch (e) {
      setError(errorCode(e))
    } finally {
      setBusy(false)
    }
  }

  if (!status) return null
  const usable = status.interfaces.filter((i) => !i.blocked)
  const blockedRadio = status.interfaces.some((i) => i.blocked)

  return (
    <Card className="space-y-3">
      <div id="servidor" className="flex flex-wrap items-center gap-2">
        <h2 className="font-semibold">{t('server.title')}</h2>
        {status.enabled ? <Badge tone="emerald">{t('server.on')}</Badge> : <Badge>{t('server.off')}</Badge>}
      </div>
      <p className="text-sm text-slate-600 dark:text-slate-300">{t('server.intro')}</p>
      <div className="space-y-1 rounded-lg bg-amber-50 p-3 text-sm text-amber-900 dark:bg-amber-950/40 dark:text-amber-200">
        <p className="font-semibold">{t('server.warningTitle')}</p>
        <p>{t('server.warningInternet')}</p>
        <p>{t('server.warningHttp')}</p>
      </div>
      {info?.os === 'windows' && <p className="text-sm text-slate-600 dark:text-slate-300">{t('server.windowsFirewall')}</p>}

      {status.enabled && status.url ? (
        <div className="flex flex-wrap items-center gap-4">
          <img src={`/api/server/qr?u=${encodeURIComponent(status.url)}`} alt={t('server.qrAlt')} className="h-40 w-40 rounded-lg bg-white p-1" />
          <div className="space-y-2 text-sm">
            <p>{t('server.openOnPhone')}</p>
            <p className="select-all break-all font-mono text-lg">{status.url}</p>
            <p className="text-slate-500">
              {t('server.interfaceUsed', { name: status.interface })} · {t('server.sessions', { count: status.sessions })}
            </p>
            <Button variant="secondary" disabled={busy} onClick={() => void toggle(false)}>
              {t('server.turnOff')}
            </Button>
          </div>
        </div>
      ) : (
        <>
          {status.radio === 'wifi' && blockedRadio && <p className="text-sm">{t('server.radioWifi')}</p>}
          {usable.length === 0 ? (
            <p className="rounded-lg bg-slate-100 p-3 text-sm dark:bg-slate-800">
              {status.radio === 'wifi' ? t('server.noInterfaceWifi') : t('server.noInterface')}
            </p>
          ) : (
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label={t('server.interface')} help={t('server.interfaceHelp')}>
                <select className={inputClass} value={choice} onChange={(e) => setChoice(e.target.value)}>
                  {status.interfaces.map((i) => (
                    <option key={i.name} value={i.name} disabled={i.blocked}>
                      {i.name} · {i.address} · {i.virtual ? t('server.virtual') : i.wireless ? t('server.wireless') : t('server.wired')}
                      {i.blocked ? ` (${t('server.blocked')})` : ''}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label={t('server.port')} help={t('server.portHelp')}>
                <input type="number" min={1024} max={65535} className={inputClass} value={port} onChange={(e) => setPort(Number(e.target.value) || 0)} />
              </Field>
            </div>
          )}
          <Button disabled={busy || !choice || usable.length === 0} onClick={() => void toggle(true)}>
            {busy ? t('common.loading') : t('server.turnOn')}
          </Button>
          <p className="text-xs text-slate-500">{t('server.offOnStart')}</p>
        </>
      )}
      {error && (
        <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-200">
          {t(`server.errors.${error}`, { defaultValue: t('common.error') })}
        </p>
      )}
    </Card>
  )
}

/** Contas do modo servidor (administradores). */
export function AccountsCard() {
  const { t } = useTranslation()
  const { me } = useAuth()
  const [accounts, setAccounts] = useState<Account[]>([])
  const [form, setForm] = useState({ username: '', password: '', role: 'user' as UserRole })
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<{ id: string; username: string; password: string } | null>(null)
  const [deleting, setDeleting] = useState<Account | null>(null)

  const load = useCallback(() => {
    api.get<Account[]>('/api/accounts').then(setAccounts).catch(() => undefined)
  }, [])

  useEffect(load, [load])

  async function run(action: () => Promise<unknown>) {
    setError(null)
    try {
      await action()
      load()
      return true
    } catch (e) {
      setError(errorCode(e))
      return false
    }
  }

  async function create() {
    if (await run(() => api.post('/api/accounts', form))) setForm({ username: '', password: '', role: 'user' })
  }

  async function saveEdit() {
    if (!editing) return
    const account = accounts.find((a) => a.id === editing.id)
    const body: Record<string, string> = {}
    if (editing.username && editing.username !== account?.username) body.username = editing.username
    if (editing.password) body.password = editing.password
    if (await run(() => api.put(`/api/accounts/${editing.id}`, body))) setEditing(null)
  }

  const name = (a: Account) => a.username ?? t('accounts.ownerNoName')

  return (
    <Card className="space-y-3">
      <h2 className="font-semibold">{t('accounts.title')}</h2>
      <p className="text-sm text-slate-600 dark:text-slate-300">{t('accounts.intro')}</p>

      <ul className="divide-y divide-slate-200 text-sm dark:divide-slate-800">
        {accounts.map((a) => (
          <li key={a.id} className="space-y-2 py-2">
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-medium">{name(a)}</span>
              {a.isOwner && <Badge tone="sky">{t('accounts.owner')}</Badge>}
              <Badge tone={a.role === 'admin' ? 'amber' : 'slate'}>{t(`accounts.roles.${a.role}`)}</Badge>
              {a.disabled && <Badge tone="red">{t('accounts.disabled')}</Badge>}
              {a.id === me?.id && !me.local && <Badge tone="emerald">{t('accounts.you')}</Badge>}
              <span className="text-xs text-slate-500">
                {a.lastLoginAt ? t('accounts.lastLogin', { date: new Date(a.lastLoginAt).toLocaleString() }) : a.isOwner && !a.canLogin ? t('accounts.ownerHint') : ''}
              </span>
              <div className="ml-auto flex flex-wrap gap-1">
                <Button variant="ghost" className="px-2 py-1" onClick={() => setEditing({ id: a.id, username: a.username ?? '', password: '' })}>
                  {a.isOwner && !a.canLogin ? t('accounts.ownerSetup') : t('accounts.edit')}
                </Button>
                {!a.isOwner && (
                  <>
                    <Button variant="ghost" className="px-2 py-1" onClick={() => void run(() => api.put(`/api/accounts/${a.id}`, { role: a.role === 'admin' ? 'user' : 'admin' }))}>
                      {a.role === 'admin' ? t('accounts.makeUser') : t('accounts.makeAdmin')}
                    </Button>
                    <Button variant="ghost" className="px-2 py-1" onClick={() => void run(() => api.put(`/api/accounts/${a.id}`, { disabled: !a.disabled }))}>
                      {a.disabled ? t('accounts.enable') : t('accounts.disable')}
                    </Button>
                    <Button variant="ghost" className="px-2 py-1 text-red-700 dark:text-red-400" onClick={() => setDeleting(a)}>
                      {t('common.delete')}
                    </Button>
                  </>
                )}
              </div>
            </div>
            {editing?.id === a.id && (
              <div className="grid gap-2 rounded-lg bg-slate-50 p-3 sm:grid-cols-[1fr_1fr_auto] dark:bg-slate-800/50">
                <Field label={t('login.username')}>
                  <input className={inputClass} autoCapitalize="none" value={editing.username} onChange={(e) => setEditing({ ...editing, username: e.target.value })} />
                </Field>
                <Field label={t('accounts.newPassword')}>
                  <input type="password" autoComplete="new-password" className={inputClass} value={editing.password} onChange={(e) => setEditing({ ...editing, password: e.target.value })} />
                </Field>
                <div className="flex items-end gap-1">
                  <Button onClick={() => void saveEdit()}>{t('common.save')}</Button>
                  <Button variant="ghost" onClick={() => setEditing(null)}>
                    {t('common.cancel')}
                  </Button>
                </div>
              </div>
            )}
          </li>
        ))}
      </ul>

      <div className="space-y-2 border-t border-slate-200 pt-3 dark:border-slate-700">
        <h3 className="text-sm font-semibold">{t('accounts.create')}</h3>
        <div className="grid gap-2 sm:grid-cols-[1fr_1fr_auto_auto]">
          <Field label={t('login.username')} help={t('accounts.usernameHelp')}>
            <input className={inputClass} autoCapitalize="none" value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} />
          </Field>
          <Field label={t('login.password')} help={t('accounts.passwordHelp')}>
            <input type="password" autoComplete="new-password" className={inputClass} value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} />
          </Field>
          <Field label={t('accounts.role')}>
            <select className={inputClass} value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value as UserRole })}>
              <option value="user">{t('accounts.roles.user')}</option>
              <option value="admin">{t('accounts.roles.admin')}</option>
            </select>
          </Field>
          <div className="flex items-start pt-6">
            <Button disabled={!form.username || !form.password} onClick={() => void create()}>
              {t('accounts.createButton')}
            </Button>
          </div>
        </div>
        <p className="text-xs text-slate-500">{t('accounts.rolesHelp')}</p>
      </div>

      {error && (
        <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-200">
          {t(`accounts.errors.${error}`, { defaultValue: t('common.error') })}
        </p>
      )}

      {deleting && (
        <Modal
          title={t('accounts.deleteTitle', { name: name(deleting) })}
          actions={
            <>
              <Button variant="secondary" onClick={() => setDeleting(null)}>
                {t('common.cancel')}
              </Button>
              <Button
                variant="danger"
                onClick={() => {
                  const id = deleting.id
                  setDeleting(null)
                  void run(() => api.delete(`/api/accounts/${id}`))
                }}
              >
                {t('common.delete')}
              </Button>
            </>
          }
        >
          {t('accounts.deleteBody')}
        </Modal>
      )}
    </Card>
  )
}
