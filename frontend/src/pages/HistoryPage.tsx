import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Icon } from '../components/ui'
import { api, spriteUrl } from '../lib/api'
import { versionName } from '../lib/games'
import type { Pokemon, TradeEvent, TradeHistoryItem } from '../lib/types'

const statusLook = {
  running: 'bg-info-soft text-info',
  completed: 'bg-ok-soft text-ok',
  partially_completed: 'bg-warn-soft text-warn',
  failed: 'bg-bad-soft text-bad',
  cancelled: 'bg-surface2 text-muted',
} as const

export default function HistoryPage() {
  const { t, i18n } = useTranslation()
  const [items, setItems] = useState<TradeHistoryItem[] | null>(null)
  const [pokemon, setPokemon] = useState<Record<string, Pokemon>>({})
  const [open, setOpen] = useState<Record<string, TradeEvent[]>>({})

  useEffect(() => {
    api.get<TradeHistoryItem[]>('/api/trades').then(setItems).catch(() => setItems([]))
    api
      .get<Pokemon[]>('/api/pokemon')
      .then((list) => setPokemon(Object.fromEntries(list.map((p) => [p.id, p]))))
      .catch(() => undefined)
  }, [])

  // Os da lixeira não vêm em /api/pokemon: busca um por um.
  useEffect(() => {
    if (!items) return
    const ids = items.flatMap((i) => i.entries.flatMap((e) => [e.sentPokemonId, e.receivedPokemonId])).filter((id): id is string => !!id && !pokemon[id])
    for (const id of new Set(ids))
      api
        .get<Pokemon>(`/api/pokemon/${id}`)
        .then((p) => setPokemon((m) => ({ ...m, [id]: p })))
        .catch(() => undefined)
  }, [items]) // só quando a lista chega

  async function toggle(id: string) {
    if (open[id]) {
      setOpen(({ [id]: _removed, ...rest }) => rest)
      return
    }
    const detail = await api.get<{ events: TradeEvent[] }>(`/api/trades/${id}`)
    setOpen((o) => ({ ...o, [id]: detail.events }))
  }

  const tile = (id: string | null, received: boolean) => {
    const p = id ? pokemon[id] : undefined
    if (!id) return <div className="h-16 w-16 rounded-lg border-[1.5px] border-dashed border-line" />
    return (
      <div
        title={p ? `${(p.nickname || p.speciesName).toUpperCase()} · ${t('ui.lv', { level: p.level })}` : undefined}
        className={`flex h-16 w-16 items-center justify-center rounded-lg ${received ? 'bg-accent-soft' : 'bg-tile'}`}
      >
        {p && <img src={spriteUrl(p.species)} alt="" className="pixelated h-14 w-14" />}
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <h1 className="font-display text-[23px] font-bold md:text-[28px]">{t('nav.history')}</h1>
      {items === null && <p className="text-muted">{t('common.loading')}</p>}
      {items?.length === 0 && (
        <div className="flex flex-col items-center gap-1.5 rounded-xl border border-line bg-surface p-10 text-center text-muted">
          <Icon name="history" className="text-4xl" />
          {t('history.empty')}
        </div>
      )}
      {items?.map((item) => {
        const sent = item.entries.map((e) => e.sentPokemonId)
        const received = item.entries.map((e) => e.receivedPokemonId).filter((id): id is string => !!id)
        const names = (ids: string[]) => ids.map((id) => (pokemon[id] ? (pokemon[id].nickname || pokemon[id].speciesName).toUpperCase() : '?')).join(', ')
        const title =
          received.length > 0 ? `${names(sent)} → ${names(received)}` : sent.length > 0 ? t('ui.history.notReceived', { names: names(sent) }) : t(`trade.result.${item.status}`)
        return (
          <div key={item.id} className="rounded-xl border border-line bg-surface">
            <div className="flex flex-wrap items-center gap-[18px] px-[18px] py-4">
              <div className="flex items-center gap-2">
                {sent.slice(0, 3).map((id, i) => (
                  <span key={`${id}-${i}`}>{tile(id, false)}</span>
                ))}
                <Icon name={received.length ? 'sync_alt' : 'east'} className="text-2xl text-muted" />
                {received.length ? received.slice(0, 3).map((id, i) => <span key={`${id}-${i}`}>{tile(id, true)}</span>) : tile(null, true)}
              </div>
              <div className="min-w-[200px] flex-1">
                <div className="text-[15.5px] font-bold [overflow-wrap:anywhere]">{title}</div>
                <div className="text-[13px] text-muted">
                  {new Date(item.startedAt).toLocaleString(i18n.language, { dateStyle: 'short', timeStyle: 'short' })} · {versionName(item.game)} · {item.trainerName} ·{' '}
                  {t('history.trades', { done: item.tradesCompleted, planned: item.tradesPlanned })} · {t('history.attempts', { count: item.attempts })}
                </div>
                {item.failureCode && item.status !== 'completed' && (
                  <div className="mt-0.5 text-[13px] text-muted">{t(`failures.${item.failureCode}`, { defaultValue: item.failureMessage ?? '' })}</div>
                )}
              </div>
              <div className="flex items-center gap-1.5">
                {item.isSimulation && <span className="flex h-6 items-center rounded-full bg-warn-soft px-2.5 text-xs font-bold text-warn">{t('demo.badge')}</span>}
                <span className={`flex h-6 items-center rounded-full px-2.5 text-xs font-bold ${statusLook[item.status]}`}>
                  {t(`trade.result.${item.status === 'running' ? 'running' : item.status}`)}
                </span>
                <button
                  type="button"
                  aria-expanded={!!open[item.id]}
                  onClick={() => void toggle(item.id)}
                  className="flex h-8 cursor-pointer items-center gap-0.5 rounded-lg bg-surface2 px-2.5 text-[13px] font-semibold"
                >
                  {t('ui.history.events')}
                  <Icon name={open[item.id] ? 'expand_less' : 'expand_more'} className="text-lg" />
                </button>
              </div>
            </div>
            {open[item.id] && (
              <ol className="flex max-h-64 flex-col gap-1 overflow-y-auto px-[18px] pb-4 font-mono text-[12.5px] text-muted">
                {open[item.id].map((e) => (
                  <li key={e.seq}>
                    {new Date(e.at).toLocaleTimeString(i18n.language)} [{e.kind}] {e.message}
                  </li>
                ))}
              </ol>
            )}
          </div>
        )
      })}
    </div>
  )
}
