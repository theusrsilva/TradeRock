import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { PokemonCard } from '../components/PokemonCard'
import { Badge, Button, Card } from '../components/ui'
import { api } from '../lib/api'
import type { Pokemon, TradeEvent, TradeHistoryItem } from '../lib/types'

const tones = { running: 'sky', completed: 'emerald', partially_completed: 'amber', failed: 'red', cancelled: 'slate' } as const

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

  async function toggle(id: string) {
    if (open[id]) {
      setOpen(({ [id]: _removed, ...rest }) => rest)
      return
    }
    const detail = await api.get<{ events: TradeEvent[] }>(`/api/trades/${id}`)
    setOpen((o) => ({ ...o, [id]: detail.events }))
  }

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">{t('history.title')}</h1>
      {items === null && <p className="text-slate-500">{t('common.loading')}</p>}
      {items?.length === 0 && <p className="text-slate-500">{t('history.empty')}</p>}
      {items?.map((item) => (
        <Card key={item.id} className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-semibold">{new Date(item.startedAt).toLocaleString(i18n.language)}</span>
            <Badge tone={tones[item.status]}>{t(`trade.result.${item.status === 'running' ? 'running' : item.status}`)}</Badge>
            {item.isSimulation && <Badge tone="amber">{t('history.simulation')}</Badge>}
            <span className="text-sm text-slate-500">
              {t(`trade.trainer.${item.game}`)} · {item.trainerName} · {t('history.trades', { done: item.tradesCompleted, planned: item.tradesPlanned })}{' '}
              · {t('history.attempts', { count: item.attempts })}
            </span>
          </div>
          {item.failureCode && item.status !== 'completed' && (
            <p className="text-sm text-slate-600 dark:text-slate-300">{t(`failures.${item.failureCode}`, { defaultValue: item.failureMessage ?? '' })}</p>
          )}
          <div className="space-y-2">
            {item.entries.map((entry) => (
              <div key={entry.tradeIndex} className="grid items-center gap-2 md:grid-cols-[1fr_auto_1fr]">
                <div>
                  <p className="mb-1 text-xs uppercase text-slate-500">{t('history.sent')}</p>
                  {pokemon[entry.sentPokemonId] ? <PokemonCard pokemon={pokemon[entry.sentPokemonId]} compact /> : '—'}
                </div>
                <span className="hidden text-2xl text-slate-400 md:block" aria-hidden>
                  ⇄
                </span>
                <div>
                  <p className="mb-1 text-xs uppercase text-slate-500">{t('history.received')}</p>
                  {entry.receivedPokemonId && pokemon[entry.receivedPokemonId] ? (
                    <PokemonCard pokemon={pokemon[entry.receivedPokemonId]} compact />
                  ) : (
                    <p className="text-sm text-slate-400">{t('history.nothing')}</p>
                  )}
                </div>
              </div>
            ))}
          </div>
          <Button variant="ghost" onClick={() => void toggle(item.id)}>
            {open[item.id] ? t('history.hide') : t('history.details')}
          </Button>
          {open[item.id] && (
            <ol className="max-h-64 space-y-1 overflow-y-auto font-mono text-xs text-slate-600 dark:text-slate-300">
              {open[item.id].map((e) => (
                <li key={e.seq}>
                  <span className="text-slate-400">{new Date(e.at).toLocaleTimeString(i18n.language)}</span> [{e.kind}] {e.message}
                </li>
              ))}
            </ol>
          )}
        </Card>
      ))}
    </div>
  )
}
