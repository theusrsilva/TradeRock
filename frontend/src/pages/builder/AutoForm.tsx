import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { SearchSelect } from '../../components/SearchSelect'
import { Button, inputClass } from '../../components/ui'
import { api, spriteUrl } from '../../lib/api'
import type { BuilderData, EncounterOption, MyTrainer, PokemonSpec, SpeciesDetails } from '../../lib/types'
import { FieldBox, StatInputs, type Issue } from './shared'

export function SpriteIcon({ species }: { species: number }) {
  return <img src={spriteUrl(species)} alt="" className="h-7 w-7 [image-rendering:pixelated]" loading="lazy" />
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

  function changeSpecies(species: number | null) {
    if (!species) return
    // Habilidade, gênero e origem dependem da espécie: voltam para "tanto faz".
    set({ species, abilitySlot: null, gender: null, encounterKey: null })
  }

  return (
    <div className="space-y-5">
      <div className="grid gap-3 sm:grid-cols-2">
        <FieldBox field="species" issue={issues.species}>
          <SearchSelect
            options={data.species.map((s) => ({ value: s.value, text: `#${s.value} ${s.text}` }))}
            value={spec.species}
            onChange={changeSpecies}
            placeholder={t('builder.searchSpecies')}
            renderIcon={(v) => <SpriteIcon species={v} />}
          />
        </FieldBox>
        <FieldBox field="nickname" issue={issues.nickname}>
          <input className={inputClass} maxLength={10} value={spec.nickname ?? ''} onChange={(e) => set({ nickname: e.target.value || null })} />
        </FieldBox>
        <FieldBox field="level" issue={issues.level}>
          <input type="number" min={1} max={100} className={inputClass} value={spec.level} onChange={(e) => set({ level: Math.max(1, Math.min(100, Number(e.target.value) || 1)) })} />
        </FieldBox>
        <FieldBox field="game">
          <select className={inputClass} value={spec.game} onChange={(e) => set({ game: e.target.value as PokemonSpec['game'], encounterKey: null })}>
            <option value="fire_red">FireRed</option>
            <option value="leaf_green">LeafGreen</option>
          </select>
        </FieldBox>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        <FieldBox field="nature" issue={issues.nature}>
          <select className={inputClass} value={spec.nature ?? ''} onChange={(e) => set({ nature: e.target.value === '' ? null : Number(e.target.value) })}>
            <option value="">{t('builder.any')}</option>
            {data.natures.map((n) => (
              <option key={n.value} value={n.value}>
                {n.text}
              </option>
            ))}
          </select>
        </FieldBox>
        <FieldBox field="ability" issue={issues.ability}>
          <select className={inputClass} value={spec.abilitySlot ?? ''} onChange={(e) => set({ abilitySlot: e.target.value === '' ? null : Number(e.target.value) })}>
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
            className={inputClass}
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

      <label className={`flex items-center gap-2 text-sm font-medium ${issues.shiny ? 'rounded-lg p-1 ring-2 ring-sky-400' : ''}`}>
        <input type="checkbox" checked={spec.shiny} onChange={(e) => set({ shiny: e.target.checked })} /> ★ {t('builder.fields.shiny')}
      </label>

      <FieldBox field="moves" issue={issues.moves}>
        <div className="grid gap-2 sm:grid-cols-2">
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

      <div className="grid gap-3 sm:grid-cols-3">
        <FieldBox field="heldItem" issue={issues.heldItem}>
          <SearchSelect options={data.items} value={spec.heldItem || null} emptyLabel={t('builder.none')} onChange={(v) => set({ heldItem: v ?? 0 })} />
        </FieldBox>
        <FieldBox field="ball" issue={issues.ball}>
          <select className={inputClass} value={spec.ball ?? ''} onChange={(e) => set({ ball: e.target.value === '' ? null : Number(e.target.value) })}>
            <option value="">{t('builder.any')}</option>
            {data.balls.map((b) => (
              <option key={b.value} value={b.value}>
                {b.text}
              </option>
            ))}
          </select>
        </FieldBox>
        <FieldBox field="language" issue={issues.language}>
          <select className={inputClass} value={spec.language} onChange={(e) => set({ language: Number(e.target.value) })}>
            {data.languages.map((l) => (
              <option key={l.value} value={l.value}>
                {l.text}
              </option>
            ))}
          </select>
        </FieldBox>
      </div>

      <FieldBox field="ivs" issue={issues.ivs}>
        <div className="space-y-2">
          <div className="flex gap-4 text-sm">
            <label className="flex items-center gap-1">
              <input type="radio" checked={spec.ivs === null} onChange={() => set({ ivs: null })} /> {t('builder.ivsAny')}
            </label>
            <label className="flex items-center gap-1">
              <input type="radio" checked={spec.ivs !== null} onChange={() => set({ ivs: [31, 31, 31, 31, 31, 31] })} /> {t('builder.ivsCustom')}
            </label>
          </div>
          {spec.ivs && <StatInputs values={spec.ivs} max={31} onChange={(ivs) => set({ ivs })} />}
        </div>
      </FieldBox>

      <FieldBox field="evs" issue={issues.evs} help={t('builder.evTotal', { total: evTotal })}>
        <StatInputs values={spec.evs} max={255} onChange={(evs) => set({ evs })} />
      </FieldBox>

      <FieldBox field="origin" issue={issues.origin} help={t('builder.originHelp')}>
        <select className={inputClass} value={spec.encounterKey ?? ''} onChange={(e) => set({ encounterKey: e.target.value || null })}>
          <option value="">{t('builder.originAuto')}</option>
          {encounters.map((e) => (
            <option key={e.key} value={e.key}>
              {t(`builder.encounterTypes.${e.type}`)} · {e.locationName || '—'} · {e.version} · Nv. {e.levelMin}
              {e.levelMax > e.levelMin ? `–${e.levelMax}` : ''}
              {e.species !== spec.species ? ` (${e.speciesName})` : ''}
            </option>
          ))}
        </select>
      </FieldBox>

      <FieldBox field="trainer" issue={issues.trainer}>
        <div className="grid gap-2 sm:grid-cols-[2fr_1fr_1fr_1fr]">
          <input className={inputClass} maxLength={7} value={spec.trainer.name} onChange={(e) => set({ trainer: { ...spec.trainer, name: e.target.value } })} aria-label={t('builder.fields.otName')} />
          <input type="number" min={0} max={65535} className={inputClass} value={spec.trainer.tid} onChange={(e) => set({ trainer: { ...spec.trainer, tid: Number(e.target.value) || 0 } })} aria-label="TID" />
          <input type="number" min={0} max={65535} className={inputClass} value={spec.trainer.sid} onChange={(e) => set({ trainer: { ...spec.trainer, sid: Number(e.target.value) || 0 } })} aria-label="SID" />
          <select className={inputClass} value={spec.trainer.gender} onChange={(e) => set({ trainer: { ...spec.trainer, gender: Number(e.target.value) } })} aria-label={t('builder.fields.otGender')}>
            <option value={0}>♂</option>
            <option value={1}>♀</option>
          </select>
        </div>
        <div className="mt-2">
          {myTrainer ? (
            <Button
              variant="ghost"
              onClick={() => set({ trainer: { name: myTrainer.name, tid: myTrainer.tid, sid: myTrainer.sid ?? 0, gender: myTrainer.gender } })}
            >
              {t('builder.useMyTrainer')}
            </Button>
          ) : (
            <p className="text-xs text-slate-500">{t('builder.noMyTrainer')}</p>
          )}
        </div>
      </FieldBox>
    </div>
  )
}
