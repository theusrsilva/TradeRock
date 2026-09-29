import { useTranslation } from 'react-i18next'
import { ContentFile } from '../../components/Markdown'
import { PokemonCard } from '../../components/PokemonCard'
import { UploadArea } from '../../components/UploadArea'
import { Button, Card } from '../../components/ui'
import type { Pokemon } from '../../lib/types'

export interface PartyState {
  party: string[] // ids, na ordem do time
  offered: string[] // ids que vão para o Switch
}

export const MAX_PARTY = 6

export function partyProblems(state: PartyState): string[] {
  const problems: string[] = []
  if (state.party.length < 2) problems.push('trade.party.needTwo')
  if (state.offered.length < 1) problems.push('trade.party.needOffered')
  return problems
}

export function PartyStep({
  pokemon,
  state,
  onChange,
  onReload,
}: {
  pokemon: Pokemon[]
  state: PartyState
  onChange: (state: PartyState) => void
  onReload: () => void
}) {
  const { t } = useTranslation()
  const byId = new Map(pokemon.map((p) => [p.id, p]))
  const available = pokemon.filter((p) => !state.party.includes(p.id))
  const offeredInOrder = state.party.filter((id) => state.offered.includes(id))

  const toggleOffered = (id: string) =>
    onChange({
      ...state,
      offered: state.offered.includes(id) ? state.offered.filter((x) => x !== id) : [...state.offered, id],
    })

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Card className="space-y-3">
        <h2 className="font-semibold">{t('trade.party.title')}</h2>
        <ContentFile name="trade/time" />
        <p className="text-sm text-slate-500">{t('trade.party.count', { count: state.party.length })}</p>
        <div className="space-y-2">
          {state.party.map((id) => {
            const p = byId.get(id)
            if (!p) return null
            const offered = state.offered.includes(id)
            const tradeNumber = offeredInOrder.indexOf(id) + 1
            return (
              <PokemonCard
                key={id}
                pokemon={p}
                compact
                highlight={offered}
                actions={
                  <>
                    <Button variant={offered ? 'primary' : 'secondary'} onClick={() => toggleOffered(id)} aria-pressed={offered}>
                      {offered ? `${t('trade.party.send')} · ${t('trade.party.tradeOrder', { n: tradeNumber })}` : t('trade.party.stays')}
                    </Button>
                    <Button
                      variant="ghost"
                      aria-label={t('trade.party.remove')}
                      title={t('trade.party.remove')}
                      onClick={() => onChange({ party: state.party.filter((x) => x !== id), offered: state.offered.filter((x) => x !== id) })}
                    >
                      ✕
                    </Button>
                  </>
                }
              />
            )
          })}
        </div>
        {state.offered.length > 0 && <p className="text-sm font-medium">{t('trade.party.summary', { count: state.offered.length })}</p>}
      </Card>

      <Card className="space-y-3">
        <h2 className="font-semibold">{t('trade.party.available')}</h2>
        <UploadArea onImported={onReload} />
        {state.party.length >= MAX_PARTY && <p className="text-sm text-amber-700 dark:text-amber-400">{t('trade.party.full')}</p>}
        <div className="max-h-[28rem] space-y-2 overflow-y-auto pr-1">
          {available.map((p) => (
            <PokemonCard
              key={p.id}
              pokemon={p}
              compact
              actions={
                <Button
                  variant="secondary"
                  disabled={state.party.length >= MAX_PARTY}
                  onClick={() => onChange({ ...state, party: [...state.party, p.id] })}
                >
                  {t('trade.party.add')}
                </Button>
              }
            />
          ))}
        </div>
      </Card>
    </div>
  )
}
