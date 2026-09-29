import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { LegalityPanel } from '../../components/Legality'
import { PokemonCard } from '../../components/PokemonCard'
import { Button, Card, inputClass } from '../../components/ui'
import { api } from '../../lib/api'
import type { BoxSummary, LegalityReport, Tag, TagColor, VaultPokemon } from '../../lib/types'
import { tagClass, tagColors } from './tagColors'

export function Details({
  pokemon,
  boxes,
  tags,
  onChanged,
  onTagsChanged,
  onExport,
  onMove,
}: {
  pokemon: VaultPokemon
  boxes: BoxSummary[]
  tags: Tag[]
  onChanged: () => void
  onTagsChanged: () => void
  onExport: (ids: string[]) => void
  onMove: (id: string, target: string) => void
}) {
  const { t, i18n } = useTranslation()
  const [report, setReport] = useState<LegalityReport | null>(null)
  const [newTag, setNewTag] = useState('')
  const [color, setColor] = useState<TagColor>('sky')

  useEffect(() => setReport(null), [pokemon.id])

  const place = pokemon.deletedAt
    ? t('vault.trash')
    : pokemon.boxId
      ? t('vault.slotLabel', { box: boxes.find((b) => b.id === pokemon.boxId)?.name ?? '?', slot: (pokemon.slot ?? 0) + 1 })
      : t('vault.inbox')

  async function toggleTag(tagId: string) {
    const has = pokemon.tagIds.includes(tagId)
    await api.post('/api/vault/tags/assign', { pokemonIds: [pokemon.id], add: has ? [] : [tagId], remove: has ? [tagId] : [] })
    onChanged()
  }

  async function createTag() {
    if (!newTag.trim()) return
    const tag = await api.post<Tag>('/api/vault/tags', { name: newTag.trim(), color })
    await api.post('/api/vault/tags/assign', { pokemonIds: [pokemon.id], add: [tag.id] })
    setNewTag('')
    onTagsChanged()
    onChanged()
  }

  return (
    <div className="space-y-3">
      <PokemonCard pokemon={pokemon} />
      <p className="text-sm text-slate-500">{t('vault.where', { place })}</p>
      {pokemon.possibleDuplicate && (
        <p className="rounded-lg bg-violet-50 p-2 text-xs text-violet-800 dark:bg-violet-950/40 dark:text-violet-200">
          ⧉ <strong>{t('vault.duplicate')}:</strong> {t('vault.duplicateHelp')}
        </p>
      )}

      <Card className="space-y-2 p-3">
        <h3 className="text-sm font-semibold">{t('vault.tags')}</h3>
        {tags.length === 0 && <p className="text-xs text-slate-500">{t('vault.noTags')}</p>}
        <div className="flex flex-wrap gap-1">
          {tags.map((tag) => {
            const on = pokemon.tagIds.includes(tag.id)
            return (
              <button
                key={tag.id}
                type="button"
                aria-pressed={on}
                onClick={() => void toggleTag(tag.id)}
                className={`rounded-full px-2 py-0.5 text-xs ${on ? tagClass[tag.color] : 'border border-dashed border-slate-300 text-slate-500 dark:border-slate-600'}`}
              >
                {on ? '✓ ' : '+ '}
                {tag.name}
              </button>
            )
          })}
        </div>
        <div className="flex gap-1">
          <input className={`${inputClass} py-1`} placeholder={t('vault.tagName')} maxLength={24} value={newTag} onChange={(e) => setNewTag(e.target.value)} />
          <select className={`${inputClass} w-24! py-1`} value={color} onChange={(e) => setColor(e.target.value as TagColor)} aria-label="Cor">
            {tagColors.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
          <Button variant="secondary" onClick={() => void createTag()} disabled={!newTag.trim()}>
            +
          </Button>
        </div>
      </Card>

      <div className="flex flex-wrap gap-2">
        {!pokemon.deletedAt && (
          <>
            <Link to={`/criar/${pokemon.id}`} className="inline-flex items-center rounded-lg border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50 dark:border-slate-600 dark:hover:bg-slate-800">
              {t('pokemon.editInBuilder')}
            </Link>
            <Button variant="secondary" onClick={() => onExport([pokemon.id])}>
              {t('vault.export')}
            </Button>
            <select
              className={`${inputClass} w-auto!`}
              value=""
              onChange={(e) => e.target.value && onMove(pokemon.id, e.target.value)}
              aria-label={t('vault.moveTo')}
            >
              <option value="">{t('vault.moveTo')}</option>
              <option value="inbox">{t('vault.inbox')}</option>
              {boxes.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </select>
          </>
        )}
        {pokemon.deletedAt ? (
          <Button variant="secondary" onClick={() => void api.post('/api/vault/restore', { ids: [pokemon.id] }).then(onChanged)}>
            {t('vault.restore')}
          </Button>
        ) : (
          <Button variant="ghost" onClick={() => void api.post('/api/vault/trash', { ids: [pokemon.id] }).then(onChanged)}>
            🗑 {t('vault.toTrash')}
          </Button>
        )}
      </div>

      <Button
        variant="ghost"
        onClick={() =>
          report ? setReport(null) : void api.get<LegalityReport>(`/api/pokemon/${pokemon.id}/legality?lang=${i18n.language}`).then(setReport)
        }
      >
        {report ? t('legality.hide') : t('legality.view')}
      </Button>
      {report && (
        <Card className="p-3">
          <LegalityPanel report={report} />
        </Card>
      )}
    </div>
  )
}
