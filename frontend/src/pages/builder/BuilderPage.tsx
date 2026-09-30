import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { LegalityPanel } from '../../components/Legality'
import { ContentFile } from '../../components/Markdown'
import { Badge, Button, Card, Modal, inputClass } from '../../components/ui'
import { ApiError, api, spriteUrl } from '../../lib/api'
import type {
  AdvancedResult,
  BuildResult,
  BuilderData,
  MyTrainer,
  Pokemon,
  PokemonSpec,
  RawPokemon,
  ShowdownImportItem,
  TrainerSpec,
} from '../../lib/types'
import { useDebouncedEffect } from '../../lib/useDebouncedEffect'
import { AdvancedForm } from './AdvancedForm'
import { AutoForm } from './AutoForm'
import { issuesFrom } from './shared'

type Mode = 'auto' | 'advanced'

function defaultSpec(trainer: TrainerSpec): PokemonSpec {
  return {
    species: 25,
    nickname: null,
    level: 20,
    nature: null,
    abilitySlot: null,
    gender: null,
    shiny: false,
    ivs: null,
    evs: [0, 0, 0, 0, 0, 0],
    moves: [],
    heldItem: 0,
    ball: null,
    language: 2,
    game: 'fire_red',
    encounterKey: null,
    trainer,
  }
}

export default function BuilderPage() {
  const { t, i18n } = useTranslation()
  const { id } = useParams()
  const [mode, setMode] = useState<Mode>('auto')
  const [data, setData] = useState<BuilderData | null>(null)
  const [myTrainer, setMyTrainer] = useState<MyTrainer | null>(null)
  const [spec, setSpec] = useState<PokemonSpec | null>(null)
  const [raw, setRaw] = useState<RawPokemon | null>(null)
  const [auto, setAuto] = useState<BuildResult | null>(null)
  const [advanced, setAdvanced] = useState<AdvancedResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [saveMessage, setSaveMessage] = useState<string | null>(null)
  const [showdownOpen, setShowdownOpen] = useState(false)

  useEffect(() => {
    api.get<BuilderData>('/api/builder/data').then(setData).catch(() => undefined)
    api.get<MyTrainer | undefined>('/api/settings/my-trainer').then((m) => setMyTrainer(m ?? null)).catch(() => undefined)
    if (id) {
      api
        .get<{ spec: PokemonSpec; raw: RawPokemon }>(`/api/builder/from/${id}`)
        .then((r) => {
          setSpec(r.spec)
          setRaw(r.raw)
          setMode('advanced') // abrir do cofre mostra o arquivo exatamente como está
        })
        .catch(() => undefined)
    } else {
      api.get<TrainerSpec>('/api/builder/default-trainer').then((tr) => setSpec(defaultSpec(tr))).catch(() => undefined)
    }
  }, [id])

  // Modo automático: gera a cada mudança.
  useDebouncedEffect(
    (isCurrent) => {
      if (mode !== 'auto' || !spec) return
      setBusy(true)
      api
        .post<BuildResult>('/api/builder/generate', { spec, language: i18n.language })
        .then((r) => {
          if (!isCurrent()) return
          setAuto(r)
          if (r.usable && r.raw) setRaw(r.raw) // o avançado só herda um resultado aproveitável
        })
        .catch(() => undefined)
        .finally(() => isCurrent() && setBusy(false))
    },
    [spec, mode, i18n.language],
  )

  // Modo avançado: analisa a cada mudança.
  useDebouncedEffect(
    (isCurrent) => {
      if (mode !== 'advanced' || !raw) return
      setBusy(true)
      api
        .post<AdvancedResult>('/api/builder/advanced', { raw, language: i18n.language })
        .then((r) => isCurrent() && setAdvanced(r))
        .catch(() => undefined)
        .finally(() => isCurrent() && setBusy(false))
    },
    [raw, mode, i18n.language],
  )

  if (!data || !spec) return <p className="text-slate-500">{t('common.loading')}</p>

  const current = mode === 'auto' ? auto : advanced
  // No automático, uma geração que falhou devolve um Pokémon "vazio": não mostrar nem salvar.
  const failed = mode === 'auto' && auto !== null && auto.errors.length === 0 && !auto.usable
  const summary = failed ? null : current?.summary ?? null
  const issues = failed ? {} : issuesFrom(current?.legality, mode === 'auto' ? auto?.differences : [])
  const errors = current?.errors ?? []
  const canSave = !!current?.data && !busy && !failed

  async function save() {
    if (!current?.data) return
    setSaveMessage(null)
    try {
      const r = await api.post<{ created: boolean; pokemon: Pokemon }>('/api/builder/save', { data: current.data })
      setSaveMessage(t(r.created ? 'builder.saved' : 'builder.savedDuplicate'))
    } catch {
      setSaveMessage(t('common.error'))
    }
  }

  function switchMode(next: Mode) {
    setSaveMessage(null)
    // Ao ir para o avançado, os campos brutos já vêm do último resultado automático.
    setMode(next)
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold">{t('builder.title')}</h1>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => setShowdownOpen(true)}>
            {t('builder.importShowdown')}
          </Button>
          <div className="inline-flex rounded-lg border border-slate-300 p-0.5 dark:border-slate-600" role="tablist">
            {(['auto', 'advanced'] as const).map((m) => (
              <button
                key={m}
                role="tab"
                aria-selected={mode === m}
                disabled={m === 'advanced' && !raw}
                onClick={() => switchMode(m)}
                className={`rounded-md px-3 py-1.5 text-sm ${mode === m ? 'bg-sky-600 text-white' : 'text-slate-600 dark:text-slate-300'}`}
              >
                {t(m === 'auto' ? 'builder.modeAuto' : 'builder.modeAdvanced')}
              </button>
            ))}
          </div>
        </div>
      </div>

      {id && <p className="text-sm text-slate-500">{t('builder.openedFrom')}</p>}

      <div className="grid gap-4 lg:grid-cols-[1fr_22rem]">
        <Card className="space-y-4">
          <ContentFile name={mode === 'auto' ? 'builder/automatico' : 'builder/avancado'} />
          {mode === 'auto' ? (
            <AutoForm data={data} spec={spec} onChange={setSpec} issues={issues} myTrainer={myTrainer} />
          ) : (
            raw && <AdvancedForm data={data} raw={raw} onChange={setRaw} issues={issues} />
          )}
        </Card>

        <div className="space-y-4 lg:sticky lg:top-4 lg:self-start">
          <Card className="space-y-3">
            <div className="flex items-center justify-between">
              <h2 className="font-semibold">{t('builder.result')}</h2>
              {busy && <span className="text-xs text-slate-500">{t('builder.generating')}</span>}
            </div>

            {errors.length > 0 && (
              <ul className="rounded-lg bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-300">
                {errors.map((e) => (
                  <li key={e}>• {t(`builder.errors.${e}`, { defaultValue: e })}</li>
                ))}
              </ul>
            )}

            {summary && (
              <div className="flex items-center gap-3">
                <img src={spriteUrl(summary.species)} alt="" className="h-20 w-20 [image-rendering:pixelated]" />
                <div className="space-y-1 text-sm">
                  <p className="font-semibold">
                    {summary.nickname} <span className="text-slate-500">#{summary.species}</span>
                  </p>
                  <p className="flex flex-wrap gap-1">
                    <Badge>{t('pokemon.level', { level: summary.level })}</Badge>
                    {summary.isShiny && <Badge tone="amber">★ {t('pokemon.shiny')}</Badge>}
                    {mode === 'advanced' && advanced?.derived && (
                      <Badge tone="sky">{data.natures[advanced.derived.nature]?.text}</Badge>
                    )}
                    {mode === 'auto' && auto?.obtained?.nature != null && <Badge tone="sky">{data.natures[auto.obtained.nature]?.text}</Badge>}
                  </p>
                  <p className="text-xs text-slate-500">{t('pokemon.ot', { name: `${summary.otName} (${summary.tid})` })}</p>
                </div>
              </div>
            )}

            {failed && auto && (
              <div className="space-y-2 rounded-lg bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-300">
                <p>{t(auto.generation === 'timeout' ? 'builder.genTimeout' : 'builder.genFailed')}</p>
                {auto.blockers && auto.blockers.length > 0 && (
                  <>
                    <p className="font-medium">{t('builder.blockersTitle')}</p>
                    <ul className="list-disc space-y-0.5 pl-5">
                      {auto.blockers.map((b) => (
                        <li key={b.code}>{t(`builder.blockers.${b.code}`, { ...b.args, species: data.species[spec.species - 1]?.text })}</li>
                      ))}
                    </ul>
                  </>
                )}
              </div>
            )}

            {mode === 'advanced' && advanced?.derived && (
              <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 rounded-lg bg-slate-50 p-2 text-xs dark:bg-slate-800">
                <dt className="col-span-2 font-semibold text-slate-500">{t('builder.derived')}</dt>
                <dt className="text-slate-500">{t('builder.fields.nature')}</dt>
                <dd>{data.natures[advanced.derived.nature]?.text}</dd>
                <dt className="text-slate-500">{t('builder.fields.gender')}</dt>
                <dd>{advanced.derived.gender === 'M' ? '♂' : advanced.derived.gender === 'F' ? '♀' : '—'}</dd>
                <dt className="text-slate-500">{t('builder.fields.ability')}</dt>
                <dd>{advanced.derived.ability}</dd>
                <dt className="text-slate-500">{t('builder.fields.shiny')}</dt>
                <dd>{advanced.derived.shiny ? '★' : '—'}</dd>
                <dt className="text-slate-500">{t('builder.method')}</dt>
                <dd>{advanced.derived.pidIvMethod}</dd>
              </dl>
            )}

            {mode === 'auto' && auto && !failed && auto.differences.length > 0 && (
              <div className="rounded-lg bg-sky-50 p-3 text-sm dark:bg-sky-950/40">
                <p className="mb-1 font-medium">{t('builder.differencesTitle')}</p>
                <ul className="space-y-0.5">
                  {auto.differences.map((d) => (
                    <li key={d.field}>
                      <strong>{t(`builder.fields.${d.field}`)}</strong>: {t('builder.requested')} <em>{d.requested || '—'}</em>, {t('builder.obtained')}{' '}
                      <em>{d.obtained || '—'}</em>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            <Button className="w-full" disabled={!canSave} onClick={() => void save()}>
              {t('builder.save')}
            </Button>
            {saveMessage && (
              <p className="text-sm text-emerald-700 dark:text-emerald-400">
                {saveMessage}{' '}
                <Link to="/pokemon" className="underline">
                  {t('nav.pokemon')}
                </Link>
              </p>
            )}
          </Card>

          {!failed && current?.legality && (
            <Card>
              <LegalityPanel report={current.legality} busy={busy} />
            </Card>
          )}
        </div>
      </div>

      {showdownOpen && (
        <ShowdownDialog
          game={spec.game}
          onClose={() => setShowdownOpen(false)}
          onPick={(picked) => {
            setSpec(picked)
            setMode('auto')
            setShowdownOpen(false)
          }}
        />
      )}
    </div>
  )
}

function ShowdownDialog({ game, onClose, onPick }: { game: PokemonSpec['game']; onClose: () => void; onPick: (spec: PokemonSpec) => void }) {
  const { t } = useTranslation()
  const [text, setText] = useState('')
  const [items, setItems] = useState<ShowdownImportItem[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function read() {
    setError(null)
    try {
      setItems(await api.post<ShowdownImportItem[]>('/api/builder/showdown', { text, game }))
    } catch (e) {
      setError(e instanceof ApiError && e.errors[0] ? t(`builder.errors.${e.errors[0]}`) : t('common.error'))
    }
  }

  return (
    <Modal
      title={t('builder.importShowdown')}
      actions={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button onClick={() => void read()} disabled={!text.trim()}>
            {t('builder.showdownRead')}
          </Button>
        </>
      }
    >
      <textarea
        className={`${inputClass} h-48 font-mono text-xs`}
        placeholder={t('builder.showdownPlaceholder')}
        value={text}
        onChange={(e) => setText(e.target.value)}
      />
      {error && <p className="mt-2 text-red-700">{error}</p>}
      {items && (
        <ul className="mt-3 max-h-60 space-y-2 overflow-y-auto">
          {items.map((item, i) => (
            <li key={i} className="flex items-center justify-between gap-2 rounded-lg border border-slate-200 p-2 dark:border-slate-700">
              <span className="text-sm">
                {item.text.split('\n')[0]}
                {item.problems.length > 0 && (
                  <span className="block text-xs text-amber-700 dark:text-amber-400">
                    {t('builder.showdownProblems')} {item.problems.map((p) => t(`builder.errors.${p}`, { defaultValue: p })).join(', ')}
                  </span>
                )}
              </span>
              {item.spec && (
                <Button variant="secondary" onClick={() => onPick(item.spec!)}>
                  {t('builder.showdownUse')}
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
    </Modal>
  )
}
