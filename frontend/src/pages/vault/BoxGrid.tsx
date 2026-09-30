import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { spriteUrl } from '../../lib/api'
import type { VaultPokemon } from '../../lib/types'
import { acceptsPokemon, draggedPokemon, startDrag } from './dnd'

const SLOTS = 30

/** Miniatura de um Pokémon (grade da caixa e listas). */
export function PokemonTile({
  pokemon,
  selected,
  focused,
  onClick,
  onToggle,
  showCheckbox,
  subtitle,
}: {
  pokemon: VaultPokemon
  selected: boolean
  focused: boolean
  onClick: (e: React.MouseEvent) => void
  onToggle: () => void
  showCheckbox: boolean
  subtitle?: string
}) {
  const legality = pokemon.legality
  return (
    <div
      draggable={!pokemon.deletedAt}
      onDragStart={(e) => startDrag(e, pokemon.id)}
      onClick={onClick}
      title={`${pokemon.nickname || pokemon.speciesName} · Nv. ${pokemon.level}`}
      className={`relative flex h-full cursor-pointer flex-col items-center justify-center rounded-lg border p-1 text-center text-[11px] leading-tight ${
        focused ? 'border-sky-500 ring-2 ring-sky-400' : selected ? 'border-sky-400 bg-sky-50 dark:bg-sky-950/40' : 'border-slate-200 bg-white hover:border-slate-400 dark:border-slate-700 dark:bg-slate-900'
      }`}
    >
      {showCheckbox && (
        <input
          type="checkbox"
          className="absolute left-1 top-1"
          checked={selected}
          onClick={(e) => e.stopPropagation()}
          onChange={onToggle}
          aria-label={pokemon.nickname || pokemon.speciesName}
        />
      )}
      <span className="absolute right-1 top-0.5 flex gap-0.5 text-[10px]">
        {pokemon.isShiny && <span title="Shiny" className="text-amber-500">★</span>}
        {legality === 'illegal' && <span className="text-red-600" title="Ilegal">✕</span>}
        {legality === 'unverifiable' && <span className="text-amber-600">?</span>}
        {pokemon.warnings.length > 0 && <span className="text-amber-600" title="Avisos">⚠</span>}
        {pokemon.possibleDuplicate && <span className="text-violet-600" title="Possível duplicata">⧉</span>}
      </span>
      <img src={spriteUrl(pokemon.species)} alt="" draggable={false} className="h-12 w-12 [image-rendering:pixelated]" loading="lazy" />
      <span className="w-full truncate font-medium">{pokemon.nickname || pokemon.speciesName}</span>
      <span className="text-slate-500">{subtitle ?? `Nv. ${pokemon.level}`}</span>
    </div>
  )
}

/** Caixa com 30 posições (6×5). Soltar numa posição move ou troca de lugar. */
export function BoxGrid({
  pokemon,
  selected,
  focused,
  onTileClick,
  onToggle,
  onDrop,
}: {
  pokemon: VaultPokemon[]
  selected: Set<string>
  focused: string | null
  onTileClick: (p: VaultPokemon, e: React.MouseEvent) => void
  onToggle: (id: string) => void
  onDrop: (pokemonId: string, slot: number) => void
}) {
  const { t } = useTranslation()
  const [over, setOver] = useState<number | null>(null)
  const bySlot = new Map(pokemon.filter((p) => p.slot !== null).map((p) => [p.slot!, p]))

  return (
    <div>
      <div className="grid grid-cols-3 gap-2 sm:grid-cols-6">
        {Array.from({ length: SLOTS }, (_, slot) => {
          const p = bySlot.get(slot)
          return (
            <div
              key={slot}
              className={`aspect-square rounded-lg ${over === slot ? 'ring-2 ring-sky-500' : ''} ${p ? '' : 'border border-dashed border-slate-300 dark:border-slate-700'}`}
              onDragOver={(e) => {
                if (acceptsPokemon(e)) {
                  e.preventDefault()
                  setOver(slot)
                }
              }}
              onDragLeave={() => setOver(null)}
              onDrop={(e) => {
                const id = draggedPokemon(e)
                setOver(null)
                if (id) onDrop(id, slot)
              }}
            >
              {p && (
                <PokemonTile
                  pokemon={p}
                  selected={selected.has(p.id)}
                  focused={focused === p.id}
                  onClick={(e) => onTileClick(p, e)}
                  onToggle={() => onToggle(p.id)}
                  showCheckbox={selected.size > 0}
                />
              )}
            </div>
          )
        })}
      </div>
      <p className="mt-2 text-xs text-slate-500">{t('vault.dragHint')}</p>
    </div>
  )
}
