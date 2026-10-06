import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Icon, inputClass } from '../../components/ui'
import type { VaultOverview } from '../../lib/types'
import { acceptsPokemon, draggedPokemon } from './dnd'

export type Scope = 'inbox' | 'trash' | string // string = id da caixa

/**
 * Caixas do cofre: lista na lateral (computador) ou fileira que rola para o lado (celular).
 * Soltar um Pokémon numa caixa move para ela.
 */
export function Sidebar({
  overview,
  scope,
  horizontal,
  onScope,
  onCreate,
  onDropPokemon,
}: {
  overview: VaultOverview
  scope: Scope
  horizontal: boolean
  onScope: (scope: Scope) => void
  onCreate: (name: string) => Promise<void>
  onDropPokemon: (pokemonId: string, target: 'inbox' | 'trash' | string) => void
}) {
  const { t } = useTranslation()
  const [creating, setCreating] = useState(false)
  const [name, setName] = useState('')
  const [dropTarget, setDropTarget] = useState<string | null>(null)

  const items: { key: Scope; label: string; icon: string; count: number }[] = [
    { key: 'inbox', label: t('vault.inbox'), icon: 'inbox', count: overview.inboxCount },
    ...overview.boxes.map((b) => ({ key: b.id, label: b.name, icon: 'inventory_2', count: b.count })),
  ]
  const trash = { key: 'trash', label: t('vault.trash'), icon: 'delete', count: overview.trashCount }

  const dropProps = (key: Scope) => ({
    onDragOver: (e: React.DragEvent) => {
      if (acceptsPokemon(e)) {
        e.preventDefault()
        setDropTarget(key)
      }
    },
    onDragLeave: () => setDropTarget(null),
    onDrop: (e: React.DragEvent) => {
      const id = draggedPokemon(e)
      setDropTarget(null)
      if (id) onDropPokemon(id, key)
    },
  })

  const createForm = (
    <form
      className="flex gap-1"
      onSubmit={(e) => {
        e.preventDefault()
        if (name.trim()) void onCreate(name.trim()).then(() => {
          setName('')
          setCreating(false)
        })
      }}
    >
      <input
        autoFocus
        className={`${inputClass} h-9! text-sm!`}
        placeholder={t('vault.boxName')}
        maxLength={24}
        value={name}
        onChange={(e) => setName(e.target.value)}
        onKeyDown={(e) => e.key === 'Escape' && setCreating(false)}
      />
      <button type="submit" aria-label={t('vault.newBox')} className="flex h-9 w-9 shrink-0 cursor-pointer items-center justify-center rounded-lg bg-accent text-accent-ink">
        <Icon name="check" />
      </button>
    </form>
  )

  if (horizontal)
    return (
      <div className="flex w-full flex-col gap-2">
        <div className="flex w-full gap-1.5 overflow-x-auto pb-0.5">
          {[...items, trash].map((it) => (
            <button
              key={it.key}
              type="button"
              onClick={() => onScope(it.key)}
              {...dropProps(it.key)}
              className={`flex h-[38px] shrink-0 cursor-pointer items-center gap-1.5 rounded-full border px-3.5 text-sm whitespace-nowrap ${
                scope === it.key ? 'border-accent bg-accent-soft font-bold' : 'border-line bg-surface'
              }`}
            >
              <Icon name={it.icon} className="text-lg" />
              {it.label}
              <span className="text-xs text-muted">{it.count}</span>
            </button>
          ))}
          <button
            type="button"
            onClick={() => setCreating(!creating)}
            aria-label={t('vault.newBox')}
            className="flex h-[38px] w-[38px] shrink-0 cursor-pointer items-center justify-center rounded-full border border-dashed border-line text-muted"
          >
            <Icon name="add" />
          </button>
        </div>
        {creating && createForm}
      </div>
    )

  const row = (it: { key: Scope; label: string; icon: string; count: number }) => (
    <button
      key={it.key}
      type="button"
      onClick={() => onScope(it.key)}
      {...dropProps(it.key)}
      className={`flex h-10 cursor-pointer items-center gap-2.5 rounded-lg px-2.5 text-left text-sm ${
        scope === it.key ? 'bg-accent-soft font-bold text-ink' : 'text-ink hover:bg-surface2'
      } ${dropTarget === it.key ? 'ring-2 ring-accent' : ''}`}
    >
      <Icon name={it.icon} className="text-[19px]" />
      <span className="flex-1 truncate">{it.label}</span>
      <span className="text-[12.5px] text-muted">{it.count}</span>
    </button>
  )

  return (
    <nav className="flex max-w-[220px] flex-[1_1_180px] flex-col gap-0.5 rounded-xl border border-line bg-surface p-2" aria-label={t('vault.title')}>
      {items.map(row)}
      {creating ? (
        <div className="px-1 py-1">{createForm}</div>
      ) : (
        <button
          type="button"
          onClick={() => setCreating(true)}
          className="flex h-10 cursor-pointer items-center gap-2.5 rounded-lg px-2.5 text-sm text-muted hover:bg-surface2"
        >
          <Icon name="add" className="text-[19px]" />
          {t('vault.newBox')}
        </button>
      )}
      <div className="mx-1 my-1.5 h-px bg-line" />
      {row(trash)}
    </nav>
  )
}
