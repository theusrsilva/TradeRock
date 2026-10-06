import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { SearchSelect } from '../../components/SearchSelect'
import { Button, Icon, Segmented, inputClass, selectClass } from '../../components/ui'
import { api, spriteUrl } from '../../lib/api'
import type { BuilderData, EncounterOption, MyTrainer, PokemonSpec, SpeciesDetails } from '../../lib/types'
import { FieldBox, StatInputs, type Issue } from './shared'

export function SpriteIcon({ species }: { species: number }) {
  return <img src={spriteUrl(species)} alt="" className="pixelated h-8 w-8" loading="lazy" />
}

export function AutoForm({
  data,
  spec,
  onChange,
  issues,
  myTrainer,
}: {
  data: BuilderData
  spec: PokemonSpec
  onChange: (spec: PokemonSpec) => void
  issues: Record<string, Issue>
  myTrainer: MyTrainer | null
}) {
  const { t } = useTranslation()
  const [details, setDetails] = useState<SpeciesDetails | null>(null)
  const [encounters, setEncounters] = useState<EncounterOption[]>([])
  const [more, setMore] = useState(false)
  const set = (patch: Partial<PokemonSpec>) => onChange({ ...spec, ...patch })

  useEffect(() => {
    api.get<SpeciesDetails>(`/api/builder/species/${spec.species}`).then(setDetails).catch(() => setDetails(null))
  }, [spec.species])

  useEffect(() => {
    api
      .get<EncounterOption[]>(`/api/builder/species/${spec.species}/encounters?game=${spec.game}`)
      .then(setEncounters)
      .catch(() => setEncounters([]))
  }, [spec.species, spec.game])

  const evTotal = spec.evs.reduce((a, b) => a + b, 0)
  // Problema num campo de "Mais opções": abre sozinho para o campo aparecer.
  const hiddenIssue = ['heldItem', 'ball', 'language', 'ivs', 'evs', 'origin', 'trainer'].some((k) => issues[k] && issues[k] !== 'changed')

  function changeSpecies(species: number | null) {
    if (!species) return
    // Habilidade, gênero e origem dependem da espécie: voltam para "tanto faz".
    set({ species, abilitySlot: null, gender: null, encounterKey: null })
  }

  return (
    <div className="flex flex-col gap-5">
      <div className="grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(220px,1fr))]">
        <FieldBox field="species" issue={issues.species}>
          <SearchSelect
            options={data.species.map((s) => ({ value: s.value, text: `#${s.value} ${s.text}` }))}
            value={spec.species}
            onChange={changeSpecies}
            placeholder={t('builder.searchSpecies')}
            renderIcon={(v) => <SpriteIcon species={v} />}
          />
        </FieldBox>
        <FieldBox field="nickname" issue={issues.nickname} label={<>{t('builder.fields.nickname')} <span className="font-normal text-muted">({t('common.optional')})</span></>}>
          <input className={inputClass} maxLength={10} value={spec.nickname ?? ''} onChange={(e) => set({ nickname: e.target.value || null })} />
        </FieldBox>
      </div>

      <div className="grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(220px,1fr))]">
        <FieldBox field="level" issue={issues.level}>
          <div className="flex h-11 items-center gap-3">
            <input
              type="range"
              min={1}
              max={100}
              value={spec.level}
              aria-label={t('builder.fields.level')}
              onChange={(e) => set({ level: Number(e.target.value) })}
              className="min-w-0 flex-1 accent-[var(--tr-accent)]"
            />
            <input
              inputMode="numeric"
              className={`${inputClass} h-10! w-16! text-center! font-bold`}
              value={spec.level}
              onChange={(e) => set({ level: Math.max(1, Math.min(100, Number(e.target.value.replace(/\D/g, '')) || 1)) })}
            />
          </div>
        </FieldBox>
        <FieldBox field="game" label={t('ui.builder.forGame')}>
          <Segmented
            className="h-11"
            value={spec.game}
            onChange={(game) => set({ game, encounterKey: null })}
            options={[
              { value: 'fire_red', label: 'FireRed' },
              { value: 'leaf_green', label: 'LeafGreen' },
            ]}
          />
        </FieldBox>
      </div>

      <div className="grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(160px,1fr))]">
        <FieldBox field="nature" issue={issues.nature}>
          <select className={selectClass} value={spec.nature ?? ''} onChange={(e) => set({ nature: e.target.value === '' ? null : Number(e.target.value) })}>
            <option value="">{t('builder.any')}</option>
            {data.natures.map((n) => (
              <option key={n.value} value={n.value}>
                {n.text}
              </option>
            ))}
          </select>
        </FieldBox>
        <FieldBox field="ability" issue={issues.ability}>
          <select className={selectClass} value={spec.abilitySlot ?? ''} onChange={(e) => set({ abilitySlot: e.target.value === '' ? null : Number(e.target.value) })}>
            <option value="">{t('builder.any')}</option>
            {details?.abilities.map((a) => (
              <option key={a.value} value={a.value}>
                {a.text}
              </option>
            ))}
          </select>
        </FieldBox>
        <FieldBox field="gender" issue={issues.gender}>
          <select
            className={selectClass}
            value={spec.gender ?? ''}
            disabled={details?.genderRatio !== 'mixed'}
            onChange={(e) => set({ gender: (e.target.value || null) as PokemonSpec['gender'] })}
          >
            <option value="">
              {details?.genderRatio === 'genderless' ? t('builder.genderless') : details?.genderRatio === 'male_only' ? t('builder.male') : details?.genderRatio === 'female_only' ? t('builder.female') : t('builder.any')}
            </option>
            <option value="M">{t('builder.male')}</option>
            <option value="F">{t('builder.female')}</option>
          </select>
        </FieldBox>
      </div>

      <button
        type="button"
        aria-pressed={spec.shiny}
        onClick={() => set({ shiny: !spec.shiny })}
        className={`inline-flex h-[38px] cursor-pointer items-center gap-2 self-start rounded-full border-[1.5px] px-3.5 text-sm font-bold ${
          spec.shiny ? 'border-[#d79c00] bg-[#fbf0c8] text-[#5a4300]' : 'border-line bg-surface'
        } ${issues.shiny ? 'ring-2 ring-info' : ''}`}
      >
        <Icon name="auto_awesome" className="text-lg text-[#d79c00]" />
        {t('builder.fields.shiny')}
      </button>

      <FieldBox field="moves" issue={issues.moves} help={t('ui.builder.movesHint')}>
        <div className="grid gap-2 [grid-template-columns:repeat(auto-fit,minmax(min(140px,100%),1fr))]">
          {[0, 1, 2, 3].map((i) => (
            <SearchSelect
              key={i}
              options={data.moves}
              value={spec.moves[i] || null}
              emptyLabel={t('builder.none')}
              placeholder={t('builder.move', { n: i + 1 })}
              onChange={(v) => {
                const moves = [0, 1, 2, 3].map((j) => spec.moves[j] ?? 0)
                moves[i] = v ?? 0
                set({ moves })
              }}
            />
          ))}
        </div>
      </FieldBox>

      {(more || hiddenIssue) && (
        <>
          <div className="grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(160px,1fr))]">
            <FieldBox field="heldItem" issue={issues.heldItem}>
              <SearchSelect options={data.items} value={spec.heldItem || null} emptyLabel={t('builder.none')} onChange={(v) => set({ heldItem: v ?? 0 })} />
            </FieldBox>
            <FieldBox field="ball" issue={issues.ball}>
              <select className={selectClass} value={spec.ball ?? ''} onChange={(e) => set({ ball: e.target.value === '' ? null : Number(e.target.value) })}>
                <option value="">{t('builder.any')}</option>
                {data.balls.map((b) => (
                  <option key={b.value} value={b.value}>
                    {b.text}
                  </option>
                ))}
              </select>
            </FieldBox>
            <FieldBox field="language" issue={issues.language}>
              <select className={selectClass} value={spec.language} onChange={(e) => set({ language: Number(e.target.value) })}>
                {data.languages.map((l) => (
                  <option key={l.value} value={l.value}>
                    {l.text}
                  </option>
                ))}
              </select>
            </FieldBox>
          </div>

          <FieldBox field="ivs" issue={issues.ivs} tip={t('ui.builder.ivTip')}>
            <div className="flex flex-col gap-2">
              <Segmented
                className="self-start"
                value={spec.ivs === null ? 'any' : 'custom'}
                onChange={(v) => set({ ivs: v === 'any' ? null : [31, 31, 31, 31, 31, 31] })}
                options={[
                  { value: 'any', label: t('builder.ivsAny') },
                  { value: 'custom', label: t('builder.ivsCustom') },
                ]}
              />
              {spec.ivs && <StatInputs values={spec.ivs} max={31} onChange={(ivs) => set({ ivs })} />}
            </div>
          </FieldBox>

          <FieldBox field="evs" issue={issues.evs} label={<span className="flex flex-1 items-center">EVs<span className={`ml-auto text-[13px] font-semibold ${evTotal > 510 ? 'text-bad' : 'text-muted'}`}>{evTotal}/510</span></span>}>
            <StatInputs values={spec.evs} max={255} onChange={(evs) => set({ evs })} />
          </FieldBox>

          <FieldBox field="origin" issue={issues.origin} help={t('builder.originHelp')}>
            <select className={selectClass} value={spec.encounterKey ?? ''} onChange={(e) => set({ encounterKey: e.target.value || null })}>
              <option value="">{t('builder.originAuto')}</option>
              {encounters.map((e) => (
                <option key={e.key} value={e.key}>
                  {t(`builder.encounterTypes.${e.type}`)} · {e.locationName || '—'} · {e.version} · {t('ui.lv', { level: e.levelMin })}
                  {e.levelMax > e.levelMin ? `–${e.levelMax}` : ''}
                  {e.species !== spec.species ? ` (${e.speciesName})` : ''}
                </option>
              ))}
            </select>
          </FieldBox>

          <FieldBox field="trainer" issue={issues.trainer}>
            <div className="grid gap-2 [grid-template-columns:repeat(auto-fit,minmax(110px,1fr))]">
              <input className={inputClass} maxLength={7} value={spec.trainer.name} onChange={(e) => set({ trainer: { ...spec.trainer, name: e.target.value } })} aria-label={t('builder.fields.otName')} />
              <input inputMode="numeric" className={inputClass} value={spec.trainer.tid} onChange={(e) => set({ trainer: { ...spec.trainer, tid: Math.min(65535, Number(e.target.value.replace(/\D/g, '')) || 0) } })} aria-label="TID" title="TID" />
              <input inputMode="numeric" className={inputClass} value={spec.trainer.sid} onChange={(e) => set({ trainer: { ...spec.trainer, sid: Math.min(65535, Number(e.target.value.replace(/\D/g, '')) || 0) } })} aria-label="SID" title="SID" />
              <select className={selectClass} value={spec.trainer.gender} onChange={(e) => set({ trainer: { ...spec.trainer, gender: Number(e.target.value) } })} aria-label={t('builder.fields.otGender')}>
                <option value={0}>♂</option>
                <option value={1}>♀</option>
              </select>
            </div>
            <div className="mt-1">
              {myTrainer ? (
                <Button
                  variant="ghost"
                  size="sm"
                  icon="badge"
                  onClick={() => set({ trainer: { name: myTrainer.name, tid: myTrainer.tid, sid: myTrainer.sid ?? 0, gender: myTrainer.gender } })}
                >
                  {t('builder.useMyTrainer')}
                </Button>
              ) : (
                <p className="text-xs text-muted">{t('builder.noMyTrainer')}</p>
              )}
            </div>
          </FieldBox>
        </>
      )}

      <button type="button" onClick={() => setMore(!more)} className="flex cursor-pointer items-center gap-1 self-start font-bold text-accent">
        <Icon name={more ? 'expand_less' : 'expand_more'} />
        {more ? t('ui.builder.lessOptions') : t('ui.builder.moreOptions')}
      </button>
    </div>
  )
}
