import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router'
import { Button, Callout, Icon, IconButton, Modal, inputClass, useIsMobile, useToast } from '../../components/ui'
import { UploadArea } from '../../components/UploadArea'
import { ApiError, api } from '../../lib/api'
import { PENDING_SAVE } from '../../components/GlobalDrop'
import { VAULT_CHANGED, takePendingSave } from '../../lib/pendingDrop'
import { requestTrade } from '../../lib/tradeRequest'
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

type Dialog =
  | null
  | { kind: 'export'; ids: string[] }
  | { kind: 'inject' }
  | { kind: 'saveImport'; file: File | null }
  | { kind: 'import' }
  | { kind: 'emptyTrash' }
  | { kind: 'rename'; id: string; name: string }

export default function VaultPage() {
  const { t } = useTranslation()
  const toast = useToast()
  const navigate = useNavigate()
  const mobile = useIsMobile()
  const [params, setParams] = useSearchParams()
  const [overview, setOverview] = useState<VaultOverview | null>(null)
  const [tags, setTags] = useState<Tag[]>([])
  const [scope, setScope] = useState<Scope>('inbox')
  const [filters, setFilters] = useState<Filters>(noFilters)
  const [showFilters, setShowFilters] = useState(false)
  const [list, setList] = useState<VaultPokemon[]>([])
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [multi, setMulti] = useState(false)
  const [focused, setFocused] = useState<string | null>(null)
  const [dialog, setDialog] = useState<Dialog>(null)
  const [importOpen, setImportOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const requestId = useRef(0)
  const importRef = useRef<HTMLDivElement>(null)

  const { text: _text, ...otherFilters } = filters
  const filtersActive = JSON.stringify(filters) !== JSON.stringify(noFilters)
  const filterCount = Object.entries(otherFilters).filter(([k, v]) => JSON.stringify(v) !== JSON.stringify(noFilters[k as keyof Filters])).length
  const isBox = scope !== 'inbox' && scope !== 'trash'
  const showGrid = isBox && !filtersActive

  const loadOverview = useCallback(() => {
    api.get<VaultOverview>('/api/vault').then(setOverview).catch(() => undefined)
  }, [])
  const loadTags = useCallback(() => {
    api.get<Tag[]>('/api/vault/tags').then(setTags).catch(() => undefined)
  }, [])

  const loadList = useCallback(() => {
    const q = new URLSearchParams()
    // Com filtro, busca em todo o cofre (menos na lixeira, que continua separada).
    q.set('scope', scope === 'trash' ? 'trash' : filtersActive ? 'all' : scope)
    if (filters.text) q.set('text', filters.text)
    if (filters.legality) q.set('legality', filters.legality)
    if (filters.origin) q.set('origin', filters.origin)
    if (filters.shiny) q.set('shiny', 'true')
    if (filters.warnings) q.set('warnings', 'true')
    if (filters.duplicates) q.set('duplicates', 'true')
    if (filters.tagIds.length) q.set('tags', filters.tagIds.join(','))
    if (filters.minLevel) q.set('minLevel', filters.minLevel)
    if (filters.maxLevel) q.set('maxLevel', filters.maxLevel)
    const id = ++requestId.current
    api
      .get<VaultPokemon[]>(`/api/vault/pokemon?${q}`)
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

  // Vindo de "Ver no cofre" (fim da troca): abre a caixa e o painel do Pokémon.
  const wanted = params.get('pokemon')
  useEffect(() => {
    if (!wanted) return
    api
      .get<VaultPokemon>(`/api/pokemon/${wanted}`)
      .then((p) => {
        setScope(p.deletedAt ? 'trash' : (p.boxId ?? 'inbox'))
        setTimeout(() => setFocused(p.id), 0)
      })
      .catch(() => undefined)
      .finally(() => setParams({}, { replace: true }))
  }, [wanted, setParams])

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

  // Fecha o menu "Importar" ao clicar fora.
  useEffect(() => {
    if (!importOpen) return
    const close = (e: MouseEvent) => {
      if (importRef.current && !importRef.current.contains(e.target as Node)) setImportOpen(false)
    }
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [importOpen])

  const focusedPokemon = useMemo(() => list.find((p) => p.id === focused) ?? null, [list, focused])
  const selectedPokemon = list.filter((p) => selected.has(p.id))
  const selecting = multi || selected.size > 0

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
    if (e.ctrlKey || e.metaKey || e.shiftKey || selecting) toggle(p.id)
    else setFocused(focused === p.id ? null : p.id)
  }

  const box = isBox ? overview?.boxes.find((b) => b.id === scope) : undefined
  const boxIndex = box ? overview!.boxes.indexOf(box) : -1
  const scopeName = scope === 'inbox' ? t('vault.inbox') : scope === 'trash' ? t('vault.trash') : (box?.name ?? '')
  const emptyText = filtersActive ? t('vault.emptyResults') : scope === 'inbox' ? t('vault.emptyInbox') : scope === 'trash' ? t('vault.emptyTrash') : t('vault.emptyBox')
  const boxName = (id: string | null) => (id ? (overview?.boxes.find((b) => b.id === id)?.name ?? '?') : t('vault.inbox'))

  async function reorder(delta: number) {
    if (!overview || boxIndex < 0) return
    const ids = overview.boxes.map((b) => b.id)
    const [x] = ids.splice(boxIndex, 1)
    ids.splice(boxIndex + delta, 0, x)
    await api.put('/api/vault/boxes/order', { ids })
    loadOverview()
  }

  if (!overview) return <p className="text-muted">{t('common.loading')}</p>

  const importOptions = [
    { icon: 'upload_file', title: t('ui.vault.importFile'), sub: t('ui.vault.importFileSub'), run: () => setDialog({ kind: 'import' }) },
    { icon: 'sd_card', title: t('vault.importSave'), sub: t('ui.vault.importSaveSub'), run: () => setDialog({ kind: 'saveImport', file: null }) },
    { icon: 'upload', title: t('vault.inject'), sub: t('ui.vault.injectSub'), run: () => setDialog({ kind: 'inject' }) },
  ]

  const filterChip = (key: 'shiny' | 'warnings' | 'duplicates', label: string, icon: string) => (
    <button
      key={key}
      type="button"
      aria-pressed={filters[key]}
      onClick={() => setFilters({ ...filters, [key]: !filters[key] })}
      className={`inline-flex h-[34px] cursor-pointer items-center gap-1.5 rounded-full border px-3 text-[13.5px] font-semibold ${filters[key] ? 'border-accent bg-accent-soft' : 'border-line bg-surface'}`}
    >
      <Icon name={filters[key] ? 'check' : icon} className="text-[17px]" />
      {label}
    </button>
  )

  const details = focusedPokemon && (
    <Details
      pokemon={focusedPokemon}
      boxes={overview.boxes}
      tags={tags}
      onChanged={reload}
      onTagsChanged={loadTags}
      onExport={(ids) => setDialog({ kind: 'export', ids })}
      onMove={(id, target) => void move(id, target)}
      onClose={() => setFocused(null)}
    />
  )

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2.5">
        <h1 className="flex-1 font-display text-[23px] font-bold md:text-[28px]">{t('vault.title')}</h1>
        <div className="relative" ref={importRef}>
          <Button icon="download" iconAfter="expand_more" aria-expanded={importOpen} onClick={() => setImportOpen(!importOpen)}>
            {t('ui.vault.import')}
          </Button>
          {importOpen && (
            <div className="absolute top-[46px] right-0 z-20 flex w-[260px] flex-col rounded-lg border border-line bg-surface p-1.5 shadow-[0_10px_30px_rgba(0,0,0,.15)]">
              {importOptions.map((o) => (
                <button
                  key={o.icon}
                  type="button"
                  onClick={() => {
                    setImportOpen(false)
                    o.run()
                  }}
                  className="flex cursor-pointer items-start gap-2.5 rounded-md p-2.5 text-left hover:bg-surface2"
                >
                  <Icon name={o.icon} className="text-accent" />
                  <span className="flex flex-col leading-tight">
                    <span className="text-sm font-bold">{o.title}</span>
                    <span className="text-[12.5px] text-muted">{o.sub}</span>
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>
        <Button
          variant="secondary"
          icon="checklist"
          aria-pressed={multi}
          className={multi ? 'border-accent! bg-accent-soft!' : ''}
          onClick={() => {
            setMulti(!multi)
            if (multi) setSelected(new Set())
          }}
        >
          {t('ui.vault.select')}
        </Button>
      </div>

      <div className="flex flex-wrap gap-2.5">
        <div className="flex h-[42px] min-w-[240px] flex-1 items-center gap-2 rounded-lg border border-line bg-surface px-3 focus-within:border-accent">
          <Icon name="search" className="text-muted" />
          <input
            value={filters.text}
            onChange={(e) => setFilters({ ...filters, text: e.target.value })}
            placeholder={t('ui.vault.search')}
            className="min-w-0 flex-1 bg-transparent text-sm outline-none"
          />
        </div>
        <Button
          variant="secondary"
          icon="tune"
          aria-expanded={showFilters}
          className={`h-[42px] ${filterCount > 0 ? 'border-accent!' : ''}`}
          onClick={() => setShowFilters(!showFilters)}
        >
          {t('vault.filters')}
          {filterCount > 0 && <span className="flex h-5 min-w-5 items-center justify-center rounded-full bg-accent px-1.5 text-xs text-accent-ink">{filterCount}</span>}
        </Button>
      </div>

      {showFilters && (
        <div className="flex flex-wrap items-center gap-2.5 rounded-xl border border-line bg-surface p-3.5">
          {filterChip('shiny', t('vault.shinyOnly'), 'auto_awesome')}
          {filterChip('warnings', t('vault.withWarnings'), 'warning')}
          {filterChip('duplicates', t('vault.duplicatesOnly'), 'content_copy')}
          <select
            className="h-[34px] cursor-pointer rounded-full border border-line bg-surface px-2 text-[13.5px]"
            value={filters.legality}
            onChange={(e) => setFilters({ ...filters, legality: e.target.value as Filters['legality'] })}
          >
            <option value="">{t('vault.anyLegality')}</option>
            <option value="legal">{t('legality.legal')}</option>
            <option value="illegal">{t('legality.illegal')}</option>
            <option value="unverifiable">{t('legality.unverifiable')}</option>
            <option value="unknown">{t('legality.unknown')}</option>
          </select>
          <select
            className="h-[34px] cursor-pointer rounded-full border border-line bg-surface px-2 text-[13.5px]"
            value={filters.origin}
            onChange={(e) => setFilters({ ...filters, origin: e.target.value as Filters['origin'] })}
          >
            <option value="">{t('vault.anyOrigin')}</option>
            {(['imported', 'received', 'created', 'demo'] as const).map((o) => (
              <option key={o} value={o}>
                {t(`pokemon.origin.${o}`)}
              </option>
            ))}
          </select>
          <div className="flex items-center gap-1.5 text-[13.5px] text-muted">
            {t('builder.fields.level')}
            <input
              inputMode="numeric"
              aria-label={t('vault.minLevel')}
              placeholder="1"
              value={filters.minLevel}
              onChange={(e) => setFilters({ ...filters, minLevel: e.target.value.replace(/\D/g, '').slice(0, 3) })}
              className="h-[34px] w-[54px] rounded-lg border border-line bg-bg px-2 text-ink outline-none focus:border-accent"
            />
            {t('ui.vault.to')}
            <input
              inputMode="numeric"
              aria-label={t('vault.maxLevel')}
              placeholder="100"
              value={filters.maxLevel}
              onChange={(e) => setFilters({ ...filters, maxLevel: e.target.value.replace(/\D/g, '').slice(0, 3) })}
              className="h-[34px] w-[54px] rounded-lg border border-line bg-bg px-2 text-ink outline-none focus:border-accent"
            />
          </div>
          {tags.map((tag) => {
            const on = filters.tagIds.includes(tag.id)
            return (
              <button
                key={tag.id}
                type="button"
                aria-pressed={on}
                onClick={() => setFilters({ ...filters, tagIds: on ? filters.tagIds.filter((x) => x !== tag.id) : [...filters.tagIds, tag.id] })}
                className={`h-[30px] cursor-pointer rounded-full px-3 text-[12.5px] font-semibold ${on ? tagClass[tag.color] : 'border border-line text-muted'}`}
              >
                {tag.name}
              </button>
            )
          })}
          <button type="button" onClick={() => setFilters({ ...noFilters, text: filters.text })} className="cursor-pointer font-semibold text-accent">
            {t('ui.vault.clear')}
          </button>
        </div>
      )}

      {error && <Callout tone="red" icon="error">{error}</Callout>}

      <div className="flex flex-wrap items-start gap-4">
        <Sidebar
          overview={overview}
          scope={scope}
          horizontal={mobile}
          onScope={setScope}
          onCreate={async (name) => {
            try {
              const created = await api.post<{ id: string }>('/api/vault/boxes', { name })
              loadOverview()
              setScope(created.id)
            } catch (e) {
              fail(e)
            }
          }}
          onDropPokemon={(id, target) => void move(id, target)}
        />

        <div className="flex min-h-[420px] min-w-0 flex-[999_1_min(340px,100%)] flex-col gap-3 rounded-xl border border-line bg-surface p-4">
          <div className="flex flex-wrap items-center gap-2">
            <div className="flex-1 text-base font-bold">{filtersActive ? t('vault.results', { count: list.length }) : scopeName}</div>
            {box && !filtersActive && (
              <span className="flex gap-0.5">
                <IconButton name="arrow_upward" label={t('vault.moveUp')} className="h-8 w-8 border-transparent" disabled={boxIndex === 0} onClick={() => void reorder(-1)} />
                <IconButton
                  name="arrow_downward"
                  label={t('vault.moveDown')}
                  className="h-8 w-8 border-transparent"
                  disabled={boxIndex === overview.boxes.length - 1}
                  onClick={() => void reorder(1)}
                />
                <IconButton name="edit" label={t('vault.rename')} className="h-8 w-8 border-transparent" onClick={() => setDialog({ kind: 'rename', id: box.id, name: box.name })} />
                <IconButton
                  name="delete"
                  label={box.count > 0 ? t('vault.boxNotEmpty') : t('vault.deleteBox')}
                  className="h-8 w-8 border-transparent"
                  disabled={box.count > 0}
                  onClick={() =>
                    void api
                      .delete(`/api/vault/boxes/${box.id}`)
                      .then(() => {
                        setScope('inbox')
                        loadOverview()
                      })
                      .catch(fail)
                  }
                />
              </span>
            )}
            <span className="text-[13px] text-muted">{t('ui.vault.count', { count: list.length })}</span>
            {scope === 'trash' && overview.trashCount > 0 && (
              <Button variant="outlineDanger" size="sm" onClick={() => setDialog({ kind: 'emptyTrash' })}>
                {t('vault.emptyTrashAction')}
              </Button>
            )}
          </div>

          {selecting && (
            <div className="flex flex-wrap items-center gap-2 rounded-lg bg-accent-soft px-2.5 py-2">
              <b className="flex-1 text-sm">{t('vault.selected', { count: selected.size })}</b>
              <Button variant="secondary" size="sm" onClick={() => setSelected(new Set(list.map((p) => p.id)))}>
                {t('ui.vault.all')}
              </Button>
              {scope === 'trash' ? (
                <Button
                  variant="secondary"
                  size="sm"
                  disabled={selected.size === 0}
                  onClick={() =>
                    void api.post('/api/vault/restore', { ids: [...selected] }).then(() => {
                      setSelected(new Set())
                      reload()
                    })
                  }
                >
                  {t('vault.restore')}
                </Button>
              ) : (
                <>
                  <select
                    className="h-8 cursor-pointer rounded-md border border-line bg-surface px-2 text-[13px]"
                    value=""
                    disabled={selected.size === 0}
                    onChange={(e) => e.target.value && void moveMany(e.target.value)}
                    aria-label={t('vault.moveTo')}
                  >
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
                      className="h-8 cursor-pointer rounded-md border border-line bg-surface px-2 text-[13px]"
                      value=""
                      disabled={selected.size === 0}
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
                  <Button variant="secondary" size="sm" disabled={selected.size === 0} onClick={() => setDialog({ kind: 'export', ids: [...selected] })}>
                    {t('vault.export')}
                  </Button>
                  <Button
                    variant="secondary"
                    size="sm"
                    disabled={selected.size === 0 || selected.size > 6}
                    title={selected.size > 6 ? t('trade.party.full') : undefined}
                    onClick={() => {
                      requestTrade([...selected])
                      navigate('/trocar')
                    }}
                  >
                    {t('ui.vault.useInTrade')}
                  </Button>
                  <Button variant="secondary" size="sm" disabled={selected.size === 0} onClick={() => setDialog({ kind: 'inject' })}>
                    {t('vault.inject')}
                  </Button>
                  <Button
                    variant="outlineDanger"
                    size="sm"
                    disabled={selected.size === 0}
                    onClick={() =>
                      void api.post('/api/vault/trash', { ids: [...selected] }).then(() => {
                        setSelected(new Set())
                        reload()
                      })
                    }
                  >
                    {t('vault.trash')}
                  </Button>
                </>
              )}
              <IconButton
                name="close"
                label={t('vault.clearSelection')}
                className="h-8 w-8"
                onClick={() => {
                  setSelected(new Set())
                  setMulti(false)
                }}
              />
            </div>
          )}

          {showGrid ? (
            <BoxGrid
              pokemon={list}
              selected={selected}
              focused={focused}
              multi={selecting}
              onTileClick={onTileClick}
              onToggle={toggle}
              onDrop={(id, slot) => void move(id, scope, slot)}
            />
          ) : list.length === 0 ? (
            <div className="flex flex-1 flex-col items-center justify-center gap-1.5 p-10 text-center text-muted">
              <Icon name="inventory_2" className="text-4xl" />
              {emptyText}
            </div>
          ) : (
            <div className="grid gap-2 [grid-template-columns:repeat(auto-fill,minmax(84px,1fr))] md:[grid-template-columns:repeat(auto-fill,minmax(104px,1fr))]">
              {list.map((p) => (
                <div key={p.id} className="aspect-[1/1.08]">
                  <PokemonTile
                    pokemon={p}
                    selected={selected.has(p.id)}
                    focused={focused === p.id}
                    onClick={(e) => onTileClick(p, e)}
                    onToggle={() => toggle(p.id)}
                    showCheckbox={selecting}
                    subtitle={filtersActive ? (p.deletedAt ? t('vault.trash') : boxName(p.boxId)) : undefined}
                  />
                </div>
              ))}
            </div>
          )}
        </div>

        {details &&
          (mobile ? (
            <>
              <div className="fixed inset-0 z-40 bg-black/45" onClick={() => setFocused(null)} />
              <div className="fixed inset-x-0 bottom-0 z-[41] max-h-[88vh] overflow-auto rounded-t-[18px] border border-line bg-surface">{details}</div>
            </>
          ) : (
            <aside className="sticky top-0 flex max-w-[380px] min-w-[300px] flex-[1_1_320px] flex-col overflow-hidden rounded-xl border border-line bg-surface">
              {details}
            </aside>
          ))}
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
      {dialog?.kind === 'rename' && (
        <Modal
          title={t('vault.rename')}
          actions={
            <>
              <Button variant="secondary" onClick={() => setDialog(null)}>
                {t('common.cancel')}
              </Button>
              <Button
                disabled={!dialog.name.trim()}
                onClick={() =>
                  void api
                    .put(`/api/vault/boxes/${dialog.id}`, { name: dialog.name.trim() })
                    .then(() => {
                      setDialog(null)
                      loadOverview()
                    })
                    .catch(fail)
                }
              >
                {t('common.save')}
              </Button>
            </>
          }
        >
          <input autoFocus className={inputClass} maxLength={24} value={dialog.name} onChange={(e) => setDialog({ ...dialog, name: e.target.value })} />
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
                    toast(t('vault.emptyTrashDone', { deleted: r.deleted, kept: r.keptBecauseOfHistory }))
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
