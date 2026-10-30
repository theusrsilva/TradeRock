import { useEffect, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, NavLink, useLocation, useNavigate } from 'react-router'
import { api } from '../lib/api'
import { useAuth } from '../lib/auth'
import { useConnection } from '../lib/connection'
import type { TradeSession } from '../lib/types'
import { useTheme } from '../theme'
import { GlobalDrop } from './GlobalDrop'
import { Button, Icon, Logo, Modal, useIsMobile } from './ui'

declare global {
  // Ponte do Photino: window.external ganha estes métodos só dentro da janela nativa.
  interface External {
    sendMessage?: (message: string) => void
    receiveMessage?: (callback: (message: string) => void) => void
  }
}

const mainLinks = [
  { to: '/', key: 'nav.home', icon: 'home', end: true },
  { to: '/trocar', key: 'nav.trade', icon: 'swap_horiz' },
  { to: '/pokemon', key: 'nav.pokemon', icon: 'inventory_2' },
  { to: '/criar', key: 'nav.builder', icon: 'add_circle' },
  { to: '/historico', key: 'nav.history', icon: 'history' },
  // Outras atividades (D26): só na janela do PC, como a configuração do rádio.
  { to: '/atividades', key: 'nav.activities', icon: 'redeem', localOnly: true },
]

/** Resumo da conexão para o menu e o Início. */
export function useConnectionSummary() {
  const { t } = useTranslation()
  const { ready, readyCount } = useConnection()
  const { me } = useAuth()
  const remote = me?.local === false
  const missing = 5 - readyCount
  return {
    ready,
    missing: remote ? 0 : missing,
    title: ready ? t('ui.status.ready') : t('ui.status.demo'),
    sub: ready
      ? t('ui.status.tested')
      : remote
        ? t('ui.status.ownerPc')
        : missing === 0
          ? t('ui.status.activate')
          : t('ui.status.missing', { count: missing }),
    short: ready ? t('ui.status.readyShort') : t('ui.status.demoShort'),
  }
}

export function Layout({ children }: { children: ReactNode }) {
  const { t } = useTranslation()
  const [closeRequested, setCloseRequested] = useState(false)
  const [closing, setClosing] = useState(false)
  const [moreOpen, setMoreOpen] = useState(false)
  const { me, logout } = useAuth()
  const remote = me?.local === false
  const mobile = useIsMobile()
  const [, setTheme, theme] = useTheme()
  const status = useConnectionSummary()
  const location = useLocation()
  const navigate = useNavigate()

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

  // Trocou de tela: fecha o "Mais" e volta ao topo.
  useEffect(() => {
    setMoreOpen(false)
    document.querySelector('main')?.scrollTo(0, 0)
  }, [location.pathname])

  async function confirmClose() {
    setClosing(true)
    try {
      await api.post('/api/app/close')
    } catch {
      setClosing(false)
    }
  }

  const toggleTheme = () => setTheme(theme === 'dark' ? 'light' : 'dark')
  const dot = status.ready ? 'bg-ok' : 'bg-warn'
  const themeIcon = theme === 'dark' ? 'dark_mode' : 'light_mode'

  const sideLink = ({ isActive }: { isActive: boolean }) =>
    `flex h-[42px] items-center gap-3 rounded-lg px-3 text-[15px] ${isActive ? 'bg-side-active font-bold text-side-ink' : 'font-medium text-side-muted hover:text-side-ink'}`

  return (
    <div className="flex h-full min-h-[560px] bg-bg text-ink">
      {!mobile && (
        <aside className="flex w-[232px] shrink-0 flex-col border-r border-side-line bg-side px-3 py-[18px] text-side-ink">
          <div className="flex items-center gap-2.5 px-2 pt-1 pb-[22px]">
            <Logo />
            <div className="font-display text-xl font-bold tracking-[.2px]">{t('app.name')}</div>
          </div>
          <nav className="flex flex-col gap-0.5">
            {mainLinks.filter((l) => !(remote && l.localOnly)).map((l) => (
              <NavLink key={l.to} to={l.to} end={l.end} className={sideLink}>
                <Icon name={l.icon} className="text-[21px]" />
                <span className="flex-1">{t(l.key)}</span>
              </NavLink>
            ))}
          </nav>
          <div className="flex-1" />
          <nav className="mb-3.5 flex flex-col gap-0.5">
            <NavLink to="/configuracoes" className={sideLink}>
              <Icon name="settings" className="text-[21px]" />
              <span className="flex-1">{t('nav.settings')}</span>
              {status.missing > 0 && <CountBadge n={status.missing} />}
            </NavLink>
            <NavLink to="/sobre" className={sideLink}>
              <Icon name="info" className="text-[21px]" />
              <span className="flex-1">{t('about.link')}</span>
            </NavLink>
          </nav>
          {!remote && me?.serverMode && (
            <Link
              to="/configuracoes?aba=avancado#servidor"
              title={t('server.onHint')}
              className="mb-2 flex items-center gap-2 rounded-lg px-3 py-1.5 text-[13px] font-semibold text-side-muted hover:text-side-ink"
            >
              <Icon name="router" className="text-[18px]" />
              {t('server.onBadge')}
            </Link>
          )}
          <Link
            to="/configuracoes"
            className="mb-3 flex items-center gap-2.5 rounded-lg border border-side-line bg-side-card px-3 py-2.5 text-left text-side-ink"
          >
            <span className={`h-2.5 w-2.5 shrink-0 rounded-full ${dot}`} />
            <span className="flex flex-col leading-tight">
              <span className="text-[13px] font-bold">{status.title}</span>
              <span className="text-xs text-side-muted">{status.sub}</span>
            </span>
          </Link>
          {remote && (
            <div className="mb-2 flex items-center gap-2 px-2 text-[13px] text-side-muted">
              <Icon name="person" className="text-[18px]" />
              <span className="flex-1 truncate">{me?.username}</span>
              <button type="button" onClick={() => void logout()} className="cursor-pointer font-semibold text-side-ink underline">
                {t('login.logout')}
              </button>
            </div>
          )}
          <div className="flex items-center gap-2 border-t border-side-line px-2 pt-2.5">
            <span className="flex-1 text-[13px] text-side-muted">{theme === 'dark' ? t('ui.theme.darkOn') : t('ui.theme.lightOn')}</span>
            <button
              type="button"
              onClick={toggleTheme}
              title={t('ui.theme.toggle')}
              aria-label={t('ui.theme.toggle')}
              className="flex h-8 w-10 cursor-pointer items-center justify-center rounded-lg border border-side-line text-side-ink"
            >
              <Icon name={themeIcon} className="text-[19px]" />
            </button>
          </div>
        </aside>
      )}

      <main className="relative flex min-w-0 flex-1 flex-col overflow-auto">
        {mobile && (
          <header className="sticky top-0 z-30 flex h-14 shrink-0 items-center gap-2.5 bg-side px-3.5 text-side-ink">
            <Logo size={26} />
            <div className="flex-1 font-display text-lg font-bold">{t('app.name')}</div>
            <button
              type="button"
              onClick={() => navigate('/configuracoes')}
              className="flex h-8 cursor-pointer items-center gap-1.5 rounded-full border border-side-line bg-side-card px-2.5 text-[12.5px] font-bold text-side-ink"
            >
              <span className={`h-2 w-2 rounded-full ${dot}`} />
              {status.short}
            </button>
            <button
              type="button"
              onClick={toggleTheme}
              aria-label={t('ui.theme.toggle')}
              className="flex h-9 w-9 cursor-pointer items-center justify-center rounded-full text-side-ink"
            >
              <Icon name={themeIcon} className="text-[21px]" />
            </button>
          </header>
        )}

        <div className="mx-auto w-full max-w-[1180px] px-3.5 pt-4 pb-6 md:px-8 md:pt-7 md:pb-12">{children}</div>

        {mobile && (
          <nav className="sticky bottom-0 z-30 mt-auto flex h-16 shrink-0 border-t border-line bg-surface pb-[env(safe-area-inset-bottom)]">
            {mainLinks.slice(0, 4).map((l) => (
              <NavLink key={l.to} to={l.to} end={l.end} className="flex-1">
                {({ isActive }) => <MobileTab icon={l.icon} label={t(`ui.mobileNav.${l.icon}`)} active={isActive} />}
              </NavLink>
            ))}
            <button type="button" className="relative flex-1 cursor-pointer" onClick={() => setMoreOpen(true)}>
              <MobileTab
                icon="menu"
                label={t('ui.mobileNav.more')}
                active={moreOpen || ['/atividades', '/historico', '/configuracoes', '/sobre'].includes(location.pathname)}
              />
              {status.missing > 0 && <span className="absolute top-1.5 right-[calc(50%-24px)] h-[9px] w-[9px] rounded-full bg-bad" />}
            </button>
          </nav>
        )}
      </main>

      {mobile && moreOpen && (
        <div className="fixed inset-0 z-45 flex items-end bg-black/40" onClick={() => setMoreOpen(false)}>
          <div
            className="flex w-full flex-col gap-0.5 rounded-t-[18px] bg-surface px-3 pt-2.5 pb-[calc(18px+env(safe-area-inset-bottom))]"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="mx-auto mt-0.5 mb-2.5 h-1 w-10 rounded-sm bg-line" />
            {!remote && <MoreItem to="/atividades" icon="redeem" label={t('nav.activities')} />}
            <MoreItem to="/historico" icon="history" label={t('nav.history')} />
            <MoreItem to="/configuracoes" icon="settings" label={t('nav.settings')} badge={status.missing || undefined} />
            <MoreItem to="/sobre" icon="info" label={t('about.link')} />
            {remote && (
              <button
                type="button"
                onClick={() => void logout()}
                className="flex h-[54px] cursor-pointer items-center gap-3.5 border-t border-line px-3 text-left text-base font-semibold text-bad"
              >
                <Icon name="logout" className="text-[23px]" />
                {t('ui.logoutAs', { name: me?.username })}
              </button>
            )}
          </div>
        </div>
      )}

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

function CountBadge({ n }: { n: number }) {
  return <span className="flex h-[22px] min-w-[22px] items-center justify-center rounded-full bg-bad px-1.5 text-xs font-bold text-white">{n}</span>
}

function MobileTab({ icon, label, active }: { icon: string; label: string; active: boolean }) {
  return (
    <span className={`flex h-full flex-col items-center justify-center gap-0.5 text-[11px] ${active ? 'font-extrabold text-ink' : 'font-semibold text-muted'}`}>
      <span className={`flex h-7 w-[52px] items-center justify-center rounded-full ${active ? 'bg-accent-soft' : ''}`}>
        <Icon name={icon} className="text-[22px]" />
      </span>
      {label}
    </span>
  )
}

function MoreItem({ to, icon, label, badge }: { to: string; icon: string; label: string; badge?: number }) {
  return (
    <Link to={to} className="flex h-[54px] items-center gap-3.5 rounded-lg px-3 text-base font-semibold">
      <Icon name={icon} className="text-[23px] text-accent" />
      <span className="flex-1">{label}</span>
      {badge ? <CountBadge n={badge} /> : null}
      <Icon name="chevron_right" className="text-muted" />
    </Link>
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
