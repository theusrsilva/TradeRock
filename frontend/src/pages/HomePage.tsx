import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { useConnectionSummary } from '../components/Layout'
import { Button, Callout, Icon } from '../components/ui'
import { api, spriteUrl } from '../lib/api'
import { useAuth } from '../lib/auth'
import { connectionKeys, requiredKeys, useConnection } from '../lib/connection'
import type { Pokemon, TradeHistoryItem, TradeSession, VaultOverview } from '../lib/types'
import { stateLook } from './settings/ConnectionChecklist'

export default function HomePage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const { me } = useAuth()
  const remote = me?.local === false
  const { items, readyCount } = useConnection()
  const status = useConnectionSummary()
  const [session, setSession] = useState<TradeSession | null>(null)
  const [last, setLast] = useState<TradeHistoryItem | null>(null)
  const [pokemon, setPokemon] = useState<Record<string, Pokemon>>({})
  const [vaultCount, setVaultCount] = useState<number | null>(null)

  useEffect(() => {
    api
      .get<TradeSession | undefined>('/api/trades/current')
      .then((s) => setSession(s ?? null))
      .catch(() => undefined)
    api
      .get<TradeHistoryItem[]>('/api/trades')
      .then((list) => {
        const done = [...list].sort((a, b) => b.startedAt.localeCompare(a.startedAt)).find((h) => h.entries.some((e) => e.receivedPokemonId))
        setLast(done ?? null)
      })
      .catch(() => undefined)
    api
      .get<Pokemon[]>('/api/pokemon')
      .then((list) => setPokemon(Object.fromEntries(list.map((p) => [p.id, p]))))
      .catch(() => undefined)
    api
      .get<VaultOverview>('/api/vault')
      .then((v) => setVaultCount(v.inboxCount + v.boxes.reduce((n, b) => n + b.count, 0)))
      .catch(() => undefined)
  }, [])

  const entry = last?.entries.find((e) => e.receivedPokemonId)
  const sent = entry ? pokemon[entry.sentPokemonId] : undefined
  const received = entry?.receivedPokemonId ? pokemon[entry.receivedPokemonId] : undefined
  const name = (p?: Pokemon) => (p ? (p.nickname || p.speciesName).toUpperCase() : '?')

  const tiles = [
    { icon: 'swap_horiz', to: '/trocar', title: t('home.startTrade'), sub: t('ui.home.tradeSub') },
    { icon: 'inventory_2', to: '/pokemon', title: t('ui.home.vault'), sub: t('ui.home.vaultSub') },
    { icon: 'add_circle', to: '/criar', title: t('nav.builder'), sub: t('ui.home.createSub') },
  ]

  return (
    <div className="flex flex-col gap-[22px]">
      <div>
        <h1 className="font-display text-[23px] leading-tight font-bold md:text-[28px]">
          {remote && me?.username ? t('ui.home.helloUser', { name: me.username }) : t('ui.home.hello')}
        </h1>
        <p className="mt-1 text-muted">{t('ui.home.intro')}</p>
      </div>

      {session?.status === 'running' && (
        <Callout tone="sky" icon="sync">
          {t('home.running')}{' '}
          <Link to="/trocar" className="font-bold underline">
            {t('home.follow')}
          </Link>
        </Callout>
      )}

      <div className="grid gap-[18px] [grid-template-columns:repeat(auto-fit,minmax(min(320px,100%),1fr))]">
        <div className="flex flex-col gap-4 rounded-xl border border-line bg-surface p-6">
          <div className="flex items-center gap-3.5">
            <div
              className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-full ${status.ready ? 'bg-ok-soft text-ok' : 'bg-warn-soft text-warn'}`}
            >
              <Icon name={status.ready ? 'check_circle' : 'build'} className="text-[28px]" />
            </div>
            <div className="flex-1">
              <div className="font-display text-[21px] leading-tight font-bold">
                {status.ready
                  ? t('ui.home.allReady')
                  : remote
                    ? t('ui.status.demo')
                    : status.missing === 0
                      ? t('ui.home.activateTitle')
                      : t('ui.home.missing', { count: status.missing })}
              </div>
              <div className="text-sm text-muted">{status.ready ? t('ui.home.readySub') : status.missing === 0 && !remote ? t('ui.conn.activateHelp') : t('ui.home.demoSub')}</div>
            </div>
          </div>
          {!remote && (
            <>
              <div className="h-2 overflow-hidden rounded bg-surface2">
                <div className={`h-full rounded ${status.ready ? 'bg-ok' : 'bg-warn'}`} style={{ width: `${(readyCount / requiredKeys.length) * 100}%` }} />
              </div>
              <div className="flex flex-wrap gap-2">
                {connectionKeys.map((k) => {
                  const look = stateLook[items[k]]
                  return (
                    <span key={k} className={`inline-flex h-[30px] items-center gap-1.5 rounded-full px-3 text-[13px] font-semibold ${look.chip}`}>
                      <Icon name={look.icon} className="text-[17px]" />
                      {t(`ui.conn.short.${k}`)}
                    </span>
                  )
                })}
              </div>
            </>
          )}
          <div className="flex flex-wrap gap-2.5">
            {!status.ready && !remote && (
              <Button size="lg" iconAfter="arrow_forward" onClick={() => navigate('/configuracoes?aba=conexao')}>
                {t('ui.home.fixNow')}
              </Button>
            )}
            <Button size="lg" variant="secondary" onClick={() => navigate('/trocar')}>
              {status.ready ? t('home.startTrade') : t('ui.home.tryDemo')}
            </Button>
          </div>
        </div>

        <div className="flex flex-col gap-3 rounded-xl border border-line bg-surface p-5">
          <div className="flex items-center justify-between">
            <div className="font-bold">{t('ui.home.lastTrade')}</div>
            <Link to="/historico" className="font-semibold text-accent">
              {t('ui.home.seeHistory')}
            </Link>
          </div>
          {entry && last ? (
            <div className="flex items-center gap-2.5 rounded-lg bg-surface2 p-3">
              {sent && <img src={spriteUrl(sent.species)} alt="" className="pixelated h-14 w-14" />}
              <Icon name="sync_alt" className="text-[22px] text-muted" />
              {received && <img src={spriteUrl(received.species)} alt="" className="pixelated h-14 w-14" />}
              <div className="min-w-0 flex-1 text-[13px] leading-snug [overflow-wrap:anywhere]">
                <div className="font-bold">
                  {name(sent)} → {name(received)}
                </div>
                <div className="text-muted">{new Date(last.startedAt).toLocaleString(i18n.language, { dateStyle: 'short', timeStyle: 'short' })}</div>
              </div>
            </div>
          ) : (
            <p className="text-sm text-muted">{t('history.empty')}</p>
          )}
          {vaultCount !== null && <div className="text-[13px] text-muted">{t('ui.home.inVault', { count: vaultCount })}</div>}
        </div>
      </div>

      <div className="grid gap-3.5 [grid-template-columns:repeat(auto-fit,minmax(220px,1fr))]">
        {tiles.map((tile) => (
          <Link
            key={tile.to}
            to={tile.to}
            className="flex flex-col items-start gap-2.5 rounded-xl border border-line bg-surface p-5 text-left hover:border-accent"
          >
            <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-accent-soft text-accent">
              <Icon name={tile.icon} className="text-[26px]" />
            </span>
            <span className="font-display text-lg font-bold">{tile.title}</span>
            <span className="text-sm text-muted">{tile.sub}</span>
          </Link>
        ))}
      </div>
    </div>
  )
}
