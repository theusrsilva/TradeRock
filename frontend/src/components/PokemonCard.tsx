import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { spriteUrl } from '../lib/api'
import type { Pokemon } from '../lib/types'
import { LegalityBadge, WarningList } from './Legality'
import { Badge } from './ui'

export function PokemonCard({
  pokemon,
  actions,
  highlight = false,
  compact = false,
}: {
  pokemon: Pokemon
  actions?: ReactNode
  highlight?: boolean
  compact?: boolean
}) {
  const { t } = useTranslation()
  const name = pokemon.nickname || pokemon.speciesName
  return (
    <div
      className={`flex items-center gap-3 rounded-xl border p-3 ${
        highlight
          ? 'border-sky-400 bg-sky-50 dark:border-sky-600 dark:bg-sky-950/40'
          : 'border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900'
      }`}
    >
      <img
        src={spriteUrl(pokemon.species)}
        alt=""
        className={`${compact ? 'h-12 w-12' : 'h-16 w-16'} shrink-0 [image-rendering:pixelated]`}
        loading="lazy"
      />
      <div className="min-w-0 flex-1 space-y-1">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span className="truncate font-semibold">{name}</span>
          {pokemon.nickname && pokemon.nickname !== pokemon.speciesName && (
            <span className="text-xs text-slate-500">({pokemon.speciesName})</span>
          )}
          <span className="text-xs text-slate-500">#{pokemon.species}</span>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <Badge>{pokemon.isEgg ? t('pokemon.egg') : t('pokemon.level', { level: pokemon.level })}</Badge>
          {pokemon.isShiny && <Badge tone="amber">★ {t('pokemon.shiny')}</Badge>}
          <Badge tone={pokemon.origin === 'received' ? 'emerald' : 'slate'}>{t(`pokemon.origin.${pokemon.origin}`)}</Badge>
          <LegalityBadge status={pokemon.legality} />
        </div>
        {!compact && (
          <p className="truncate text-xs text-slate-500 dark:text-slate-400">{t('pokemon.ot', { name: pokemon.otName })}</p>
        )}
        <WarningList warnings={pokemon.warnings ?? []} />
      </div>
      {actions && <div className="flex shrink-0 flex-col gap-1 sm:flex-row">{actions}</div>}
    </div>
  )
}
