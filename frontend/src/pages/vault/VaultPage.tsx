import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Card, Modal, inputClass } from '../../components/ui'
import { UploadArea } from '../../components/UploadArea'
import { ApiError, api } from '../../lib/api'
import { PENDING_SAVE } from '../../components/GlobalDrop'
import { VAULT_CHANGED, takePendingSave } from '../../lib/pendingDrop'
import type { LegalityStatus, PokemonOrigin, Tag, VaultOverview, VaultPokemon } from '../../lib/types'
import { BoxGrid, PokemonTile } from './BoxGrid'
import { Details } from './Details'
import { ExportDialog } from './ExportDialog'
import { InjectDialog } from './InjectDialog'
import { SaveImportDialog } from './SaveImportDialog'
import { Sidebar, type Scope } from './Sidebar'
import { tagClass } from './tagColors'

interface Filters {
  text: string
  legality: LegalityStatus | 'unknown' | ''
  origin: PokemonOrigin | ''
  shiny: boolean
  warnings: boolean
  duplicates: boolean
  tagIds: string[]
  minLevel: string
  maxLevel: string
}

const noFilters: Filters = { text: '', legality: '', origin: '', shiny: false, warnings: false, duplicates: false, tagIds: [], minLevel: '', maxLevel: '' }

type Dialog = null | { kind: 'export'; ids: string[] } | { kind: 'inject' } | { kind: 'saveImport'; file: File | null } | { kind: 'import' } | { kind: 'emptyTrash' }

export default function VaultPage() {
  const { t } = useTranslation()
  const [overview, setOverview] = useState<VaultOverview | null>(null)
  const [tags, setTags] = useState<Tag[]>([])
  const [scope, setScope] = useState<Scope>('inbox')
  const [filters, setFilters] = useState<Filters>(noFilters)
  const [list, setList] = useState<VaultPokemon[]>([])
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [focused, setFocused] = useState<string | null>(null)
  const [dialog, setDialog] = useState<Dialog>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const requestId = useRef(0)

  const filtersActive = JSON.stringify(filters) !== JSON.stringify(noFilters)
  const isBox = scope !== 'inbox' && scope !== 'trash'
  const showGrid = isBox && !filtersActive

  const loadOverview = useCallback(() => {
    api.get<VaultOverview>('/api/vault').then(setOverview).catch(() => undefined)
  }, [])
  const loadTags = useCallback(() => {
    api.get<Tag[]>('/api/vault/tags').then(setTags).catch(() => undefined)
  }, [])

  const loadList = useCallback(() => {
    const params = new URLSearchParams()
    // Com filtro, busca em todo o cofre (menos na lixeira, que continua separada).
    params.set('scope', scope === 'trash' ? 'trash' : filtersActive ? 'all' : scope)
    if (filters.text) params.set('text', filters.text)
    if (filters.legality) params.set('legality', filters.legality)
    if (filters.origin) params.set('origin', filters.origin)
    if (filters.shiny) params.set('shiny', 'true')
    if (filters.warnings) params.set('warnings', 'true')
    if (filters.duplicates) params.set('duplicates', 'true')
    if (filters.tagIds.length) params.set('tags', filters.tagIds.join(','))
    if (filters.minLevel) params.set('minLevel', filters.minLevel)
    if (filters.maxLevel) params.set('maxLevel', filters.maxLevel)
    const id = ++requestId.current
    api
      .get<VaultPokemon[]>(`/api/vault/pokemon?${params}`)
      .then((r) => id === requestId.current && setList(r))
      .catch(() => undefined)
  }, [scope, filters, filtersActive])

  const reload = useCallback(() => {
    loadOverview()
    loadList()
  }, [loadOverview, loadList])

  useEffect(() => {
    loadOverview()
    loadTags()
    const pending = takePendingSave()
    if (pending) setDialog({ kind: 'saveImport', file: pending })
  }, [loadOverview, loadTags])

  useEffect(() => {
    const timer = setTimeout(loadList, filters.text ? 250 : 0)
    return () => clearTimeout(timer)
  }, [loadList, filters.text])

  useEffect(() => {
    window.addEventListener(VAULT_CHANGED, reload)
    const openPending = () => {
      const pending = takePendingSave()
      if (pending) setDialog({ kind: 'saveImport', file: pending })
    }
    window.addEventListener(PENDING_SAVE, openPending)
    return () => {
      window.removeEventListener(VAULT_CHANGED, reload)
      window.removeEventListener(PENDING_SAVE, openPending)
    }
  }, [reload])

  useEffect(() => {
    setSelected(new Set())
    setFocused(null)
  }, [scope])

  const focusedPokemon = useMemo(() => list.find((p) => p.id === focused) ?? null, [list, focused])
  const selectedPokemon = list.filter((p) => selected.has(p.id))

  const fail = (e: unknown) => {
    const code = e instanceof ApiError ? e.code : null
    setError(code ? t(`vault.errors.${code}`, { defaultValue: t('common.error') }) : t('common.error'))
    setTimeout(() => setError(null), 4000)
  }

  async function move(pokemonId: string, target: 'inbox' | 'trash' | string, slot: number | null = null) {
    try {
      if (target === 'trash') await api.post('/api/vault/trash', { ids: [pokemonId] })
      else await api.post(`/api/vault/pokemon/${pokemonId}/move`, { boxId: target === 'inbox' ? null : target, slot })
      reload()
    } catch (e) {
      fail(e)
    }
  }

  async function moveMany(target: string) {
    for (const id of selected) {
      try {
        await api.post(`/api/vault/pokemon/${id}/move`, { boxId: target === 'inbox' ? null : target, slot: null })
      } catch (e) {
        fail(e)
        break
      }
    }
    setSelected(new Set())
    reload()
  }

  async function applyTag(tagId: string, add: boolean) {
    await api.post('/api/vault/tags/assign', { pokemonIds: [...selected], add: add ? [tagId] : [], remove: add ? [] : [tagId] })
    reload()
  }

  function toggle(id: string) {
    setSelected((s) => {
      const next = new Set(s)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  function onTileClick(p: VaultPokemon, e: React.MouseEvent) {
    if (e.ctrlKey || e.metaKey || e.shiftKey || selected.size > 0) toggle(p.id)
    else setFocused(p.id)
  }

  const scopeName =
    scope === 'inbox' ? t('vault.inbox') : scope === 'trash' ? t('vault.trash') : overview?.boxes.find((b) => b.id === scope)?.name ?? ''
  const emptyText = filtersActive ? t('vault.emptyResults') : scope === 'inbox' ? t('vault.emptyInbox') : scope === 'trash' ? t('vault.emptyTrash') : t('vault.emptyBox')

  const boxName = (id: string | null) => (id ? overview?.boxes.find((b) => b.id === id)?.name ?? '?' : t('vault.inbox'))

  if (!overview) return <p className="text-slate-500">{t('common.loading')}</p>

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-semibold">{t('vault.title')}</h1>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => setDialog({ kind: 'import' })}>
            {t('vault.importFiles')}
          </Button>
          <Button variant="secondary" onClick={() => setDialog({ kind: 'saveImport', file: null })}>
            {t('vault.importSave')}
          </Button>
          <Button variant="secondary" onClick={() => setDialog({ kind: 'inject' })}>
            {t('vault.inject')}
          </Button>
        </div>
      </div>

      <Card className="space-y-2 p-3">
        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-[1fr_12rem_12rem_7rem_7rem]">
          <input
            className={`${inputClass} sm:col-span-2 lg:col-span-1`}
            placeholder={t('vault.search')}
            value={filters.text}
            onChange={(e) => setFilters({ ...filters, text: e.target.value })}
          />
          <select className={inputClass} value={filters.legality} onChange={(e) => setFilters({ ...filters, legality: e.target.value as Filters['legality'] })}>
            <option value="">{t('vault.anyLegality')}</option>
            <option value="legal">{t('legality.legal')}</option>
            <option value="illegal">{t('legality.illegal')}</option>
            <option value="unverifiable">{t('legality.unverifiable')}</option>
            <option value="unknown">{t('legality.unknown')}</option>
          </select>
          <select className={inputClass} value={filters.origin} onChange={(e) => setFilters({ ...filters, origin: e.target.value as Filters['origin'] })}>
            <option value="">{t('vault.anyOrigin')}</option>
            {(['imported', 'received', 'created', 'demo'] as const).map((o) => (
              <option key={o} value={o}>
                {t(`pokemon.origin.${o}`)}
              </option>
            ))}
          </select>
          <input type="number" min={1} max={100} className={inputClass} placeholder={t('vault.minLevel')} value={filters.minLevel} onChange={(e) => setFilters({ ...filters, minLevel: e.target.value })} />
          <input type="number" min={1} max={100} className={inputClass} placeholder={t('vault.maxLevel')} value={filters.maxLevel} onChange={(e) => setFilters({ ...filters, maxLevel: e.target.value })} />
        </div>
        <div className="flex flex-wrap items-center gap-3 text-sm">
          {(['shiny', 'warnings', 'duplicates'] as const).map((key) => (
            <label key={key} className="flex items-center gap-1">
              <input type="checkbox" checked={filters[key]} onChange={(e) => setFilters({ ...filters, [key]: e.target.checked })} />
              {t(key === 'shiny' ? 'vault.shinyOnly' : key === 'warnings' ? 'vault.withWarnings' : 'vault.duplicatesOnly')}
            </label>
          ))}
          {tags.map((tag) => {
            const on = filters.tagIds.includes(tag.id)
            return (
              <button
                key={tag.id}
                type="button"
                aria-pressed={on}
                onClick={() => setFilters({ ...filters, tagIds: on ? filters.tagIds.filter((x) => x !== tag.id) : [...filters.tagIds, tag.id] })}
                className={`rounded-full px-2 py-0.5 text-xs ${on ? tagClass[tag.color] : 'border border-slate-300 text-slate-500 dark:border-slate-600'}`}
              >
                {tag.name}
              </button>
            )
          })}
          {filtersActive && (
            <Button variant="ghost" onClick={() => setFilters(noFilters)}>
              {t('vault.clearFilters')}
            </Button>
          )}
        </div>
      </Card>

      {error && <p className="rounded-lg bg-red-50 p-2 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-300">{error}</p>}
      {notice && <p className="rounded-lg bg-emerald-50 p-2 text-sm text-emerald-800 dark:bg-emerald-950/40 dark:text-emerald-300">{notice}</p>}

      <div className="grid gap-4 lg:grid-cols-[14rem_1fr_20rem]">
        <Card className="p-3">
          <Sidebar
            overview={overview}
            scope={scope}
            onScope={setScope}
            onCreate={async (name) => {
              try {
                const box = await api.post<{ id: string }>('/api/vault/boxes', { name })
                loadOverview()
                setScope(box.id)
              } catch (e) {
                fail(e)
              }
            }}
            onRename={async (id, name) => {
              try {
                await api.put(`/api/vault/boxes/${id}`, { name })
                loadOverview()
              } catch (e) {
                fail(e)
              }
            }}
            onDelete={async (id) => {
              try {
                await api.delete(`/api/vault/boxes/${id}`)
                setScope('inbox')
                loadOverview()
              } catch (e) {
                fail(e)
              }
            }}
            onReorder={async (ids) => {
              await api.put('/api/vault/boxes/order', { ids })
              loadOverview()
            }}
            onDropPokemon={(id, target) => void move(id, target)}
          />
        </Card>

        <Card className="space-y-3 p-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="font-semibold">{filtersActive ? t('vault.results', { count: list.length }) : scopeName}</h2>
            <div className="flex flex-wrap items-center gap-2 text-sm">
              {list.length > 0 && (
                <Button variant="ghost" onClick={() => setSelected(new Set(list.map((p) => p.id)))}>
                  {t('vault.selectAll')}
                </Button>
              )}
              {scope === 'trash' && overview.trashCount > 0 && (
                <Button variant="danger" onClick={() => setDialog({ kind: 'emptyTrash' })}>
                  {t('vault.emptyTrashAction')}
                </Button>
              )}
            </div>
          </div>

          {selected.size > 0 && (
            <div className="flex flex-wrap items-center gap-2 rounded-lg bg-sky-50 p-2 text-sm dark:bg-sky-950/40">
              <strong>{t('vault.selected', { count: selected.size })}</strong>
              {scope === 'trash' ? (
                <Button variant="secondary" onClick={() => void api.post('/api/vault/restore', { ids: [...selected] }).then(() => { setSelected(new Set()); reload() })}>
                  {t('vault.restore')}
                </Button>
              ) : (
                <>
                  <select className={`${inputClass} w-auto! py-1`} value="" onChange={(e) => e.target.value && void moveMany(e.target.value)} aria-label={t('vault.moveTo')}>
                    <option value="">{t('vault.moveTo')}</option>
                    <option value="inbox">{t('vault.inbox')}</option>
                    {overview.boxes.map((b) => (
                      <option key={b.id} value={b.id}>
                        {b.name}
                      </option>
                    ))}
                  </select>
                  {tags.length > 0 && (
                    <select
                      className={`${inputClass} w-auto! py-1`}
                      value=""
                      onChange={(e) => {
                        const [op, id] = e.target.value.split(':')
                        if (id) void applyTag(id, op === 'add')
                      }}
                      aria-label={t('vault.addTag')}
                    >
                      <option value="">{t('vault.tags')}…</option>
                      {tags.map((tag) => (
                        <option key={`add-${tag.id}`} value={`add:${tag.id}`}>
                          + {tag.name}
                        </option>
                      ))}
                      {tags.map((tag) => (
                        <option key={`rm-${tag.id}`} value={`remove:${tag.id}`}>
                          − {tag.name}
                        </option>
                      ))}
                    </select>
                  )}
                  <Button variant="secondary" onClick={() => setDialog({ kind: 'export', ids: [...selected] })}>
                    {t('vault.export')}
                  </Button>
                  <Button variant="secondary" onClick={() => setDialog({ kind: 'inject' })}>
                    {t('vault.inject')}
                  </Button>
                  <Button variant="ghost" onClick={() => void api.post('/api/vault/trash', { ids: [...selected] }).then(() => { setSelected(new Set()); reload() })}>
                    🗑 {t('vault.toTrash')}
                  </Button>
                </>
              )}
              <Button variant="ghost" onClick={() => setSelected(new Set())}>
                {t('vault.clearSelection')}
              </Button>
            </div>
          )}

          {showGrid ? (
            <BoxGrid
              pokemon={list}
              selected={selected}
              focused={focused}
              onTileClick={onTileClick}
              onToggle={toggle}
              onDrop={(id, slot) => void move(id, scope, slot)}
            />
          ) : list.length === 0 ? (
            <p className="py-8 text-center text-sm text-slate-500">{emptyText}</p>
          ) : (
            <div className="grid grid-cols-3 gap-2 sm:grid-cols-5 xl:grid-cols-6">
              {list.map((p) => (
                <div key={p.id} className="aspect-square">
                  <PokemonTile
                    pokemon={p}
                    selected={selected.has(p.id)}
                    focused={focused === p.id}
                    onClick={(e) => onTileClick(p, e)}
                    onToggle={() => toggle(p.id)}
                    showCheckbox={selected.size > 0}
                    subtitle={filtersActive ? (p.deletedAt ? t('vault.trash') : boxName(p.boxId)) : undefined}
                  />
                </div>
              ))}
            </div>
          )}
        </Card>

        <Card className="p-3">
          <h2 className="mb-2 font-semibold">{t('vault.details')}</h2>
          {focusedPokemon ? (
            <Details
              pokemon={focusedPokemon}
              boxes={overview.boxes}
              tags={tags}
              onChanged={reload}
              onTagsChanged={loadTags}
              onExport={(ids) => setDialog({ kind: 'export', ids })}
              onMove={(id, target) => void move(id, target)}
            />
          ) : (
            <p className="text-sm text-slate-500">{t('vault.noSelection')}</p>
          )}
        </Card>
      </div>

      {dialog?.kind === 'export' && <ExportDialog ids={dialog.ids} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'inject' && (
        <InjectDialog pokemon={selectedPokemon.length > 0 ? selectedPokemon : focusedPokemon ? [focusedPokemon] : []} onClose={() => setDialog(null)} />
      )}
      {dialog?.kind === 'saveImport' && <SaveImportDialog initialFile={dialog.file} onClose={() => setDialog(null)} onImported={reload} />}
      {dialog?.kind === 'import' && (
        <Modal title={t('vault.importFiles')} actions={<Button onClick={() => setDialog(null)}>OK</Button>}>
          <UploadArea onImported={reload} />
        </Modal>
      )}
      {dialog?.kind === 'emptyTrash' && (
        <Modal
          title={t('vault.emptyTrashAction')}
          actions={
            <>
              <Button variant="secondary" onClick={() => setDialog(null)}>
                {t('common.cancel')}
              </Button>
              <Button
                variant="danger"
                onClick={() =>
                  void api.post<{ deleted: number; keptBecauseOfHistory: number }>('/api/vault/trash/empty').then((r) => {
                    setDialog(null)
                    setNotice(t('vault.emptyTrashDone', { deleted: r.deleted, kept: r.keptBecauseOfHistory }))
                    setTimeout(() => setNotice(null), 5000)
                    reload()
                  })
                }
              >
                {t('vault.emptyTrashAction')}
              </Button>
            </>
          }
        >
          {t('vault.emptyTrashConfirm')}
        </Modal>
      )}
    </div>
  )
}
