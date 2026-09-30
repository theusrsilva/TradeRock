import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, inputClass } from '../../components/ui'
import type { VaultOverview } from '../../lib/types'
import { acceptsPokemon, draggedPokemon } from './dnd'

export type Scope = 'inbox' | 'trash' | string // string = id da caixa

export function Sidebar({
  overview,
  scope,
  onScope,
  onCreate,
  onRename,
  onDelete,
  onReorder,
  onDropPokemon,
}: {
  overview: VaultOverview
  scope: Scope
  onScope: (scope: Scope) => void
  onCreate: (name: string) => Promise<void>
  onRename: (id: string, name: string) => Promise<void>
  onDelete: (id: string) => Promise<void>
  onReorder: (ids: string[]) => Promise<void>
  onDropPokemon: (pokemonId: string, target: 'inbox' | 'trash' | string) => void
}) {
  const { t } = useTranslation()
  const [creating, setCreating] = useState(false)
  const [name, setName] = useState('')
  const [editing, setEditing] = useState<string | null>(null)
  const [dropTarget, setDropTarget] = useState<string | null>(null)

  const item = (key: Scope, label: string, count: number, extra?: React.ReactNode) => (
    <li key={key}>
      <div
        role="button"
        tabIndex={0}
        onClick={() => onScope(key)}
        onKeyDown={(e) => e.key === 'Enter' && onScope(key)}
        onDragOver={(e) => {
          if (acceptsPokemon(e)) {
            e.preventDefault()
            setDropTarget(key)
          }
        }}
        onDragLeave={() => setDropTarget(null)}
        onDrop={(e) => {
          const id = draggedPokemon(e)
          setDropTarget(null)
          if (id) onDropPokemon(id, key)
        }}
        className={`group flex cursor-pointer items-center justify-between gap-2 rounded-lg px-3 py-2 text-sm ${
          scope === key ? 'bg-sky-100 font-medium text-sky-900 dark:bg-sky-900/50 dark:text-sky-200' : 'hover:bg-slate-100 dark:hover:bg-slate-800'
        } ${dropTarget === key ? 'ring-2 ring-sky-500' : ''}`}
      >
        <span className="truncate">{label}</span>
        <span className="flex items-center gap-1">
          {extra}
          <span className="text-xs text-slate-500">{count}</span>
        </span>
      </div>
    </li>
  )

  const ids = overview.boxes.map((b) => b.id)
  const move = (index: number, delta: number) => {
    const next = [...ids]
    const [x] = next.splice(index, 1)
    next.splice(index + delta, 0, x)
    void onReorder(next)
  }

  return (
    <nav className="space-y-3" aria-label={t('vault.title')}>
      <ul className="space-y-0.5">{item('inbox', t('vault.inbox'), overview.inboxCount)}</ul>

      <ul className="space-y-0.5">
        {overview.boxes.map((box, index) =>
          editing === box.id ? (
            <li key={box.id} className="px-1">
              <input
                autoFocus
                className={inputClass}
                defaultValue={box.name}
                maxLength={24}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') void onRename(box.id, (e.target as HTMLInputElement).value).then(() => setEditing(null))
                  if (e.key === 'Escape') setEditing(null)
                }}
                onBlur={(e) => void onRename(box.id, e.target.value).then(() => setEditing(null))}
              />
            </li>
          ) : (
            item(
              box.id,
              box.name,
              box.count,
              scope === box.id && (
                <span className="flex gap-0.5 text-xs">
                  <button type="button" title={t('vault.moveUp')} disabled={index === 0} className="px-1 disabled:opacity-30" onClick={(e) => { e.stopPropagation(); move(index, -1) }}>▲</button>
                  <button type="button" title={t('vault.moveDown')} disabled={index === ids.length - 1} className="px-1 disabled:opacity-30" onClick={(e) => { e.stopPropagation(); move(index, 1) }}>▼</button>
                  <button type="button" title={t('vault.rename')} className="px-1" onClick={(e) => { e.stopPropagation(); setEditing(box.id) }}>✎</button>
                  <button type="button" title={box.count > 0 ? t('vault.boxNotEmpty') : t('vault.deleteBox')} disabled={box.count > 0} className="px-1 disabled:opacity-30" onClick={(e) => { e.stopPropagation(); void onDelete(box.id) }}>✕</button>
                </span>
              ),
            )
          ),
        )}
      </ul>

      {creating ? (
        <form
          className="flex gap-1 px-1"
          onSubmit={(e) => {
            e.preventDefault()
            if (name.trim()) void onCreate(name.trim()).then(() => { setName(''); setCreating(false) })
          }}
        >
          <input autoFocus className={inputClass} placeholder={t('vault.boxName')} maxLength={24} value={name} onChange={(e) => setName(e.target.value)} />
          <Button type="submit" variant="secondary">+</Button>
        </form>
      ) : (
        <Button variant="ghost" className="w-full justify-start" onClick={() => setCreating(true)}>
          + {t('vault.newBox')}
        </Button>
      )}

      <ul className="space-y-0.5 border-t border-slate-200 pt-3 dark:border-slate-700">{item('trash', `🗑 ${t('vault.trash')}`, overview.trashCount)}</ul>
    </nav>
  )
}
