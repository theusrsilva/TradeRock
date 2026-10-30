import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { LegalityPanel } from '../../components/Legality'
import { ContentFile } from '../../components/Markdown'
import { Button, Callout, HelpTip, Icon, Modal, Toggle, inputClass, useToast } from '../../components/ui'
import { ApiError, api, spriteUrl } from '../../lib/api'
import { gameOf } from '../../lib/games'
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
  const toast = useToast()
  const [showdownOpen, setShowdownOpen] = useState(false)

  useEffect(() => {
    if (id) {
      api
        .get<{ spec: PokemonSpec; raw: RawPokemon | null }>(`/api/builder/from/${id}`)
        .then((r) => {
          setSpec(r.spec)
          setRaw(r.raw)
          // Abrir do cofre mostra o arquivo exatamente como está (modo avançado, só no FireRed/LeafGreen).
          if (r.raw) setMode('advanced')
        })
        .catch(() => undefined)
    } else {
      api.get<TrainerSpec>('/api/builder/default-trainer').then((tr) => setSpec(defaultSpec(tr))).catch(() => undefined)
    }
  }, [id])

  // Listas e "Meu treinador" do jogo escolhido.
  const game = spec?.game ?? 'fire_red'
  const family = gameOf(game).family
  useEffect(() => {
    api.get<BuilderData>(`/api/builder/data?game=${game}`).then(setData).catch(() => undefined)
  }, [game])
  useEffect(() => {
    api.get<MyTrainer | undefined>(`/api/settings/my-trainer?game=${family}`).then((m) => setMyTrainer(m ?? null)).catch(() => undefined)
  }, [family])

  /** Outro jogo: o treinador vem do jogo novo e o que só vale no jogo anterior volta para "tanto faz". */
  async function changeGame(next: PokemonSpec['game']) {
    if (!spec) return
    if (gameOf(next).family === gameOf(spec.game).family) {
      setSpec({ ...spec, game: next, encounterKey: null })
      return
    }
    const trainer = await api.get<TrainerSpec>(`/api/builder/default-trainer?game=${next}`).catch(() => spec.trainer)
    setMode('auto')
    setRaw(null)
    setSpec({ ...spec, game: next, form: 0, encounterKey: null, heldItem: 0, moves: [], ball: null, abilitySlot: null, evs: [0, 0, 0, 0, 0, 0], trainer })
  }

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

  if (!data || !spec) return <p className="text-muted">{t('common.loading')}</p>

  const current = mode === 'auto' ? auto : advanced
  // No automático, uma geração que falhou devolve um Pokémon "vazio": não mostrar nem salvar.
  const failed = mode === 'auto' && auto !== null && auto.errors.length === 0 && !auto.usable
  const summary = failed ? null : current?.summary ?? null
  const issues = failed ? {} : issuesFrom(current?.legality, mode === 'auto' ? auto?.differences : [])
  const errors = current?.errors ?? []
  const canSave = !!current?.data && !busy && !failed
  const legality = failed ? null : current?.legality ?? null
  const natureOut =
    mode === 'advanced' && advanced?.derived
      ? data.natures[advanced.derived.nature]?.text
      : mode === 'auto' && auto?.obtained?.nature != null
        ? data.natures[auto.obtained.nature]?.text
        : undefined
  const previewSpecies = summary?.species ?? (mode === 'auto' ? spec.species : raw?.species ?? spec.species)
  const previewName = (summary?.nickname ?? data.species[previewSpecies - 1]?.text ?? '').toUpperCase()
  const legalLook =
    !legality || busy
      ? { cls: 'bg-surface2 text-muted', icon: 'hourglass_top', label: t('builder.generating') }
      : legality.status === 'legal'
        ? legality.checks.some((c) => c.severity === 'fishy')
          ? { cls: 'bg-warn-soft text-warn', icon: 'warning', label: t('ui.builder.legalAttention') }
          : { cls: 'bg-ok-soft text-ok', icon: 'verified', label: t('legality.legal') }
        : legality.status === 'illegal'
          ? { cls: 'bg-bad-soft text-bad', icon: 'error', label: t('legality.illegal') }
          : { cls: 'bg-warn-soft text-warn', icon: 'help', label: t('legality.unverifiable') }

  async function save() {
    if (!current?.data) return
    try {
      const r = await api.post<{ created: boolean; pokemon: Pokemon }>('/api/builder/save', { data: current.data, game: spec?.game })
      toast(t(r.created ? 'builder.saved' : 'builder.savedDuplicate'))
    } catch {
      toast(t('common.error'))
    }
  }

  return (
    <div className="flex flex-col gap-[18px]">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="flex-1 font-display text-[23px] font-bold md:text-[28px]">{t('builder.title')}</h1>
        <Button variant="secondary" icon="content_paste" onClick={() => setShowdownOpen(true)}>
          {t('builder.importShowdown')}
        </Button>
        <span className="inline-flex h-10 items-center rounded-lg border border-line bg-surface px-3">
          <Toggle
            small
            on={mode === 'advanced'}
            disabled={(mode === 'auto' && !raw) || !data.advanced}
            onChange={(on) => setMode(on ? 'advanced' : 'auto')}
            label={t('builder.modeAdvanced')}
          />
        </span>
      </div>
      {mode === 'advanced' && (
        <Callout tone="sky" icon="construction">
          {t('ui.builder.advancedBanner')}
        </Callout>
      )}
      {id && <p className="text-sm text-muted">{t('builder.openedFrom')}</p>}

      <div className="flex flex-wrap items-start gap-[18px]">
        <div className="flex min-w-0 flex-[999_1_min(460px,100%)] flex-col gap-5 rounded-xl border border-line bg-surface p-[22px]">
          <details className="text-sm">
            <summary className="cursor-pointer font-semibold text-accent">{t('ui.builder.howItWorks')}</summary>
            <div className="mt-2">
              <ContentFile name={mode === 'auto' ? 'builder/automatico' : 'builder/avancado'} />
            </div>
          </details>
          {mode === 'auto' ? (
            <AutoForm data={data} spec={spec} onChange={setSpec} issues={issues} myTrainer={myTrainer} onGameChange={(g) => void changeGame(g)} />
          ) : (
            raw && <AdvancedForm data={data} raw={raw} onChange={setRaw} issues={issues} />
          )}
        </div>

        <div className="flex w-full flex-[1_1_300px] flex-col gap-3.5 md:sticky md:top-0 md:max-w-[380px]">
          <div className="flex flex-col items-center gap-2.5 rounded-xl border border-line bg-surface p-5 text-center">
            <div className="flex h-[140px] w-[140px] items-center justify-center rounded-xl bg-tile">
              <img src={spriteUrl(previewSpecies)} alt="" className={`pixelated h-32 w-32 ${busy ? 'opacity-60' : ''}`} />
            </div>
            <div>
              <div className="font-display text-[22px] font-bold">{previewName}</div>
              <div className="text-[13px] text-muted">
                #{String(previewSpecies).padStart(3, '0')}
                {summary && ` · ${t('ui.lv', { level: summary.level })}`}
                {natureOut && ` · ${natureOut}`}
                {summary?.isShiny && ' · ★'}
              </div>
              {summary && <div className="text-[12.5px] text-muted">{t('pokemon.ot', { name: `${summary.otName} (${summary.tid})` })}</div>}
            </div>

            {errors.length > 0 && (
              <Callout tone="red" icon="error" className="w-full text-left">
                <ul>
                  {errors.map((e) => (
                    <li key={e}>{t(`builder.errors.${e}`, { defaultValue: e })}</li>
                  ))}
                </ul>
              </Callout>
            )}

            {failed && auto && (
              <Callout tone="red" icon="error" className="w-full text-left">
                <p>{t(auto.generation === 'timeout' ? 'builder.genTimeout' : 'builder.genFailed')}</p>
                {auto.blockers && auto.blockers.length > 0 && (
                  <>
                    <p className="mt-1 font-bold">{t('builder.blockersTitle')}</p>
                    <ul className="list-disc space-y-0.5 pl-5">
                      {auto.blockers.map((b) => (
                        <li key={b.code}>{t(`builder.blockers.${b.code}`, { ...b.args, species: data.species[spec.species - 1]?.text })}</li>
                      ))}
                    </ul>
                  </>
                )}
              </Callout>
            )}

            {mode === 'advanced' && advanced?.derived && (
              <div className="flex w-full flex-col gap-[3px] rounded-lg bg-surface2 px-3 py-2.5 text-left">
                <div className="mb-0.5 text-[11.5px] font-bold tracking-[.6px] text-muted uppercase">{t('builder.derived')}</div>
                {[
                  [t('builder.fields.nature'), data.natures[advanced.derived.nature]?.text],
                  [t('builder.fields.gender'), advanced.derived.gender === 'M' ? '♂' : advanced.derived.gender === 'F' ? '♀' : '—'],
                  [t('builder.fields.ability'), advanced.derived.ability],
                  [t('builder.fields.shiny'), advanced.derived.shiny ? '★' : '—'],
                  [t('builder.method'), advanced.derived.pidIvMethod],
                ].map(([k, v]) => (
                  <div key={k} className="flex text-[13px]">
                    <span className="flex-1 text-muted">{k}</span>
                    <b>{v}</b>
                  </div>
                ))}
              </div>
            )}

            {mode === 'auto' && auto && !failed && auto.differences.length > 0 && (
              <Callout tone="sky" icon="info" className="w-full text-left">
                <p className="mb-1 font-bold">{t('builder.differencesTitle')}</p>
                <ul className="space-y-0.5">
                  {auto.differences.map((d) => (
                    <li key={d.field}>
                      <strong>{t(`builder.fields.${d.field}`)}</strong>: {t('builder.requested')} <em>{d.requested || '—'}</em>, {t('builder.obtained')}{' '}
                      <em>{d.obtained || '—'}</em>
                    </li>
                  ))}
                </ul>
              </Callout>
            )}

            <Button size="lg" icon="inventory_2" className="h-12 w-full" disabled={!canSave} onClick={() => void save()}>
              {t('builder.save')}
            </Button>
            <Link to="/pokemon" className="text-[13px] font-semibold text-accent">
              {t('ui.home.vault')}
            </Link>
          </div>

          {!failed && (
            <div className="flex flex-col gap-2.5 rounded-xl border border-line bg-surface px-[18px] py-4">
              <div className="flex items-center gap-2">
                <span className={`flex h-[26px] items-center gap-1 rounded-full px-2.5 text-[13px] font-bold ${legalLook.cls}`}>
                  <Icon name={legalLook.icon} className="text-base" />
                  {legalLook.label}
                </span>
                <span className="flex-1" />
                <HelpTip>{t('ui.builder.legalTip', { version: legality?.pkhexVersion ?? '' })}</HelpTip>
              </div>
              {legality && <LegalityPanel report={legality} busy={busy} />}
            </div>
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
      wide
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
        className={`${inputClass} h-48! py-2! font-mono text-[13px]!`}
        placeholder={t('builder.showdownPlaceholder')}
        value={text}
        onChange={(e) => setText(e.target.value)}
      />
      {error && <p className="mt-2 text-bad">{error}</p>}
      {items && (
        <ul className="mt-3 max-h-60 space-y-2 overflow-y-auto">
          {items.map((item, i) => (
            <li key={i} className="flex items-center justify-between gap-2 rounded-lg border border-line p-2">
              <span className="text-sm">
                {item.text.split('\n')[0]}
                {item.problems.length > 0 && (
                  <span className="block text-xs text-warn">
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
