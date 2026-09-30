import { useEffect, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, NavLink } from 'react-router'
import { api } from '../lib/api'
import { useAppInfo } from '../lib/appInfo'
import { useAuth } from '../lib/auth'
import type { Engine, TradeSession } from '../lib/types'
import { GlobalDrop } from './GlobalDrop'
import { Badge, Button, Modal } from './ui'

declare global {
  // Ponte do Photino: window.external ganha estes métodos só dentro da janela nativa.
  interface External {
    sendMessage?: (message: string) => void
    receiveMessage?: (callback: (message: string) => void) => void
  }
}

const links = [
  { to: '/', key: 'nav.home', end: true },
  { to: '/trocar', key: 'nav.trade' },
  { to: '/pokemon', key: 'nav.pokemon' },
  { to: '/criar', key: 'nav.builder' },
  { to: '/historico', key: 'nav.history' },
  { to: '/configuracoes', key: 'nav.settings' },
]

export function Layout({ children }: { children: ReactNode }) {
  const { t } = useTranslation()
  const [demo, setDemo] = useState(false)
  const [closeRequested, setCloseRequested] = useState(false)
  const [closing, setClosing] = useState(false)
  const info = useAppInfo()
  const { me, logout } = useAuth()
  const remote = me?.local === false

  useEffect(() => {
    api
      .get<Engine[]>('/api/engines')
      .then((engines) => setDemo(engines.find((e) => e.selected)?.isSimulation ?? false))
      .catch(() => undefined)
  }, [])

  // Janela nativa: o backend avisa quando o usuário tenta fechar durante uma troca.
  useEffect(() => {
    window.external?.receiveMessage?.((message: string) => {
      try {
        if ((JSON.parse(message) as { type?: string }).type === 'close_requested') setCloseRequested(true)
      } catch {
        // mensagem desconhecida
      }
    })
  }, [])

  // Navegador (--no-window): avisa antes de sair da página durante uma troca.
  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (sessionStorage.getItem('traderock.tradeRunning') === '1') e.preventDefault()
    }
    window.addEventListener('beforeunload', handler)
    return () => window.removeEventListener('beforeunload', handler)
  }, [])

  async function confirmClose() {
    setClosing(true)
    try {
      await api.post('/api/app/close')
    } catch {
      setClosing(false)
    }
  }

  return (
    <div className="flex min-h-full flex-col bg-slate-50 text-slate-900 dark:bg-slate-950 dark:text-slate-100">
      <header className="border-b border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
          <div className="flex items-center gap-2">
            <img src="/icon.svg" alt="" className="h-7 w-7" />
            <span className="text-lg font-semibold">{t('app.name')}</span>
            {demo && (
              <span title={t('demo.explain')}>
                <Badge tone="amber">{t('demo.badge')}</Badge>
              </span>
            )}
          </div>
          <nav className="flex flex-wrap gap-1 text-sm">
            {links.map((link) => (
              <NavLink
                key={link.to}
                to={link.to}
                end={link.end}
                className={({ isActive }) =>
                  `rounded-lg px-3 py-1.5 ${
                    isActive
                      ? 'bg-sky-100 font-medium text-sky-800 dark:bg-sky-900/50 dark:text-sky-300'
                      : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'
                  }`
                }
              >
                {t(link.key)}
              </NavLink>
            ))}
          </nav>
          <div className="ml-auto flex items-center gap-2 text-sm">
            {remote ? (
              <>
                <span className="text-slate-600 dark:text-slate-300">{t('login.as', { name: me?.username })}</span>
                <Button variant="ghost" className="px-2 py-1" onClick={() => void logout()}>
                  {t('login.logout')}
                </Button>
              </>
            ) : (
              me?.serverMode && (
                <Link to="/configuracoes#servidor" title={t('server.onHint')}>
                  <Badge tone="sky">{t('server.onBadge')}</Badge>
                </Link>
              )
            )}
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-7xl flex-1 px-4 py-6">{children}</main>
      <footer className="mx-auto w-full max-w-7xl px-4 pb-4 text-xs text-slate-500">
        TradeRock {info?.version} · AGPL-3.0 ·{' '}
        <Link to="/sobre" className="underline hover:text-sky-700 dark:hover:text-sky-400">
          {t('about.link')}
        </Link>
      </footer>
      <GlobalDrop />

      {closeRequested && (
        <Modal
          title={t('close.title')}
          actions={
            <>
              <Button variant="secondary" disabled={closing} onClick={() => setCloseRequested(false)}>
                {t('close.keep')}
              </Button>
              <Button variant="danger" disabled={closing} onClick={() => void confirmClose()}>
                {closing ? t('trade.live.cancelling') : t('close.confirm')}
              </Button>
            </>
          }
        >
          {t('close.body')}
        </Modal>
      )}
    </div>
  )
}

/** Marca, para o aviso do navegador, se há uma troca rodando. */
export function markTradeRunning(session: TradeSession | null) {
  try {
    sessionStorage.setItem('traderock.tradeRunning', session?.status === 'running' ? '1' : '0')
  } catch {
    // ignora
  }
}
