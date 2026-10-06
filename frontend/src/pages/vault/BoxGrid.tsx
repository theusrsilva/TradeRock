import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Icon } from '../../components/ui'
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
  const { t } = useTranslation()
  const name = (pokemon.nickname || pokemon.speciesName).toUpperCase()
  const warn =
    pokemon.legality === 'illegal'
      ? t('legality.illegal')
      : pokemon.warnings[0]
        ? t(`warnings.${pokemon.warnings[0].code}`, pokemon.warnings[0].args)
        : pokemon.possibleDuplicate
          ? t('vault.duplicate')
          : null
  return (
    <div
      role="button"
      tabIndex={0}
      draggable={!pokemon.deletedAt}
      onDragStart={(e) => startDrag(e, pokemon.id)}
      onClick={onClick}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          onClick(e as unknown as React.MouseEvent)
        }
      }}
      aria-pressed={showCheckbox ? selected : undefined}
      title={`${name} · ${t('ui.lv', { level: pokemon.level })}${warn ? ` · ${warn}` : ''}`}
      className={`relative flex h-full cursor-pointer flex-col items-center justify-center gap-px rounded-lg border-2 p-1 text-center leading-tight ${
        focused || selected ? 'border-accent bg-accent-soft' : 'border-transparent bg-tile hover:border-accent'
      }`}
    >
      {showCheckbox && (
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation()
            onToggle()
          }}
          aria-label={name}
          className={`absolute top-[5px] left-[5px] flex h-5 w-5 cursor-pointer items-center justify-center rounded-[5px] border-[1.5px] ${selected ? 'border-accent bg-accent text-accent-ink' : 'border-line bg-surface'}`}
        >
          {selected && <Icon name="check" className="text-base" />}
        </button>
      )}
      {warn && (
        <span title={warn} className={`absolute top-[5px] right-1.5 ${pokemon.legality === 'illegal' ? 'text-bad' : pokemon.warnings.length ? 'text-warn' : 'text-violet-500'}`}>
          <Icon name={pokemon.legality === 'illegal' ? 'error' : pokemon.warnings.length ? 'warning' : 'content_copy'} className="text-[17px]" />
        </span>
      )}
      {pokemon.isShiny && (
        <span title={t('pokemon.shiny')} className="absolute right-2 bottom-[26px] text-[#e0a100]">
          <Icon name="auto_awesome" className="text-base" />
        </span>
      )}
      <img src={spriteUrl(pokemon.species)} alt="" draggable={false} className="pixelated h-12 w-12 md:h-16 md:w-16" loading="lazy" />
      <span className="w-full truncate text-xs font-bold">{name}</span>
      <span className="text-[11.5px] text-muted">{subtitle ?? (pokemon.isEgg ? t('pokemon.egg') : t('ui.lv', { level: pokemon.level }))}</span>
    </div>
  )
}

/** Caixa com 30 posições (6×5). Soltar numa posição move ou troca de lugar. */
export function BoxGrid({
  pokemon,
  selected,
  focused,
  multi,
  onTileClick,
  onToggle,
  onDrop,
}: {
  pokemon: VaultPokemon[]
  selected: Set<string>
  focused: string | null
  multi: boolean
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
              className={`aspect-[1/1.08] rounded-lg ${over === slot ? 'ring-2 ring-accent' : ''} ${p ? '' : 'border-[1.5px] border-dashed border-line'}`}
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
                  showCheckbox={multi}
                />
              )}
            </div>
          )
        })}
      </div>
      <p className="mt-2 text-xs text-muted">{t('vault.dragHint')}</p>
    </div>
  )
}
