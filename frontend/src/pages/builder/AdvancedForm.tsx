import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { SearchSelect } from '../../components/SearchSelect'
import { Button, inputClass } from '../../components/ui'
import { api } from '../../lib/api'
import type { BuilderData, Option, RawPokemon } from '../../lib/types'
import { SpriteIcon } from './AutoForm'
import { FieldBox, StatInputs, type Issue } from './shared'

const hex = (n: number) => (n >>> 0).toString(16).toUpperCase().padStart(8, '0')

export function AdvancedForm({
  data,
  raw,
  onChange,
  issues,
}: {
  data: BuilderData
  raw: RawPokemon
  onChange: (raw: RawPokemon) => void
  issues: Record<string, Issue>
}) {
  const { t } = useTranslation()
  const [locations, setLocations] = useState<Option[]>([])
  const [pidText, setPidText] = useState(hex(raw.pid))
  const set = (patch: Partial<RawPokemon>) => onChange({ ...raw, ...patch })

  useEffect(() => setPidText(hex(raw.pid)), [raw.pid])

  useEffect(() => {
    api.get<Option[]>(`/api/builder/locations?version=${raw.version}`).then(setLocations).catch(() => setLocations([]))
  }, [raw.version])

  return (
    <div className="space-y-5">
      <div className="grid gap-3 sm:grid-cols-3">
        <FieldBox field="species" issue={issues.species}>
          <SearchSelect
            options={data.species.map((s) => ({ value: s.value, text: `#${s.value} ${s.text}` }))}
            value={raw.species}
            onChange={(v) => v && set({ species: v })}
            renderIcon={(v) => <SpriteIcon species={v} />}
          />
        </FieldBox>
        <FieldBox field="nickname" issue={issues.nickname}>
          <input className={inputClass} maxLength={10} value={raw.nickname ?? ''} onChange={(e) => set({ nickname: e.target.value || null })} />
        </FieldBox>
        <FieldBox field="level" issue={issues.level}>
          <input type="number" min={1} max={100} className={inputClass} value={raw.level} onChange={(e) => set({ level: Math.max(1, Math.min(100, Number(e.target.value) || 1)) })} />
        </FieldBox>
      </div>

      <div className="grid gap-3 sm:grid-cols-[2fr_1fr]">
        <FieldBox field="pid" issue={issues.pid}>
          <div className="flex gap-2">
            <input
              className={`${inputClass} font-mono`}
              value={pidText}
              maxLength={8}
              onChange={(e) => {
                const text = e.target.value.toUpperCase().replace(/[^0-9A-F]/g, '')
                setPidText(text)
                if (text.length > 0) set({ pid: parseInt(text, 16) >>> 0 })
              }}
            />
            <Button variant="secondary" onClick={() => set({ pid: (Math.random() * 2 ** 32) >>> 0 })}>
              {t('builder.randomPid')}
            </Button>
          </div>
        </FieldBox>
        <label className="flex items-end gap-2 pb-2 text-sm">
          <input type="checkbox" checked={raw.abilityBit} onChange={(e) => set({ abilityBit: e.target.checked })} /> {t('builder.fields.abilityBit')}
        </label>
      </div>

      <FieldBox field="ivs" issue={issues.ivs}>
        <StatInputs values={raw.ivs} max={31} onChange={(ivs) => set({ ivs })} />
      </FieldBox>
      <FieldBox field="evs" issue={issues.evs} help={t('builder.evTotal', { total: raw.evs.reduce((a, b) => a + b, 0) })}>
        <StatInputs values={raw.evs} max={255} onChange={(evs) => set({ evs })} />
      </FieldBox>

      <FieldBox field="moves" issue={issues.moves}>
        <div className="grid gap-2">
          {[0, 1, 2, 3].map((i) => (
            <div key={i} className="grid grid-cols-[1fr_5rem] gap-2">
              <SearchSelect
                options={data.moves}
                value={raw.moves[i] || null}
                emptyLabel={t('builder.none')}
                placeholder={t('builder.move', { n: i + 1 })}
                onChange={(v) => {
                  const moves = [...raw.moves]
                  moves[i] = v ?? 0
                  set({ moves })
                }}
              />
              <select
                className={inputClass}
                aria-label={t('builder.fields.ppUps')}
                value={raw.ppUps[i]}
                onChange={(e) => {
                  const ppUps = [...raw.ppUps]
                  ppUps[i] = Number(e.target.value)
                  set({ ppUps })
                }}
              >
                {[0, 1, 2, 3].map((n) => (
                  <option key={n} value={n}>
                    +{n}
                  </option>
                ))}
              </select>
            </div>
          ))}
        </div>
      </FieldBox>

      <div className="grid gap-3 sm:grid-cols-3">
        <FieldBox field="heldItem" issue={issues.heldItem}>
          <SearchSelect options={data.items} value={raw.heldItem || null} emptyLabel={t('builder.none')} onChange={(v) => set({ heldItem: v ?? 0 })} />
        </FieldBox>
        <FieldBox field="ball" issue={issues.ball}>
          <select className={inputClass} value={raw.ball} onChange={(e) => set({ ball: Number(e.target.value) })}>
            {data.balls.map((b) => (
              <option key={b.value} value={b.value}>
                {b.text}
              </option>
            ))}
          </select>
        </FieldBox>
        <FieldBox field="language" issue={issues.language}>
          <select className={inputClass} value={raw.language} onChange={(e) => set({ language: Number(e.target.value) })}>
            {data.languages.map((l) => (
              <option key={l.value} value={l.value}>
                {l.text}
              </option>
            ))}
          </select>
        </FieldBox>
      </div>

      <div className="grid gap-3 sm:grid-cols-4">
        <FieldBox field="otName" issue={issues.trainer}>
          <input className={inputClass} maxLength={7} value={raw.otName} onChange={(e) => set({ otName: e.target.value })} />
        </FieldBox>
        <FieldBox field="otGender">
          <select className={inputClass} value={raw.otGender} onChange={(e) => set({ otGender: Number(e.target.value) })}>
            <option value={0}>♂</option>
            <option value={1}>♀</option>
          </select>
        </FieldBox>
        <FieldBox field="tid" issue={issues.trainer}>
          <input type="number" min={0} max={65535} className={inputClass} value={raw.tid} onChange={(e) => set({ tid: Number(e.target.value) || 0 })} />
        </FieldBox>
        <FieldBox field="sid" issue={issues.trainer}>
          <input type="number" min={0} max={65535} className={inputClass} value={raw.sid} onChange={(e) => set({ sid: Number(e.target.value) || 0 })} />
        </FieldBox>
      </div>

      <div className="grid gap-3 sm:grid-cols-4">
        <FieldBox field="version" issue={issues.origin}>
          <select className={inputClass} value={raw.version} onChange={(e) => set({ version: e.target.value })}>
            {data.versions.map((v) => (
              <option key={v} value={v}>
                {v}
              </option>
            ))}
          </select>
        </FieldBox>
        <FieldBox field="metLocation" issue={issues.origin}>
          <SearchSelect options={locations} value={raw.metLocation} onChange={(v) => set({ metLocation: v ?? 0 })} />
        </FieldBox>
        <FieldBox field="metLevel" issue={issues.level}>
          <input type="number" min={0} max={100} className={inputClass} value={raw.metLevel} onChange={(e) => set({ metLevel: Math.max(0, Math.min(100, Number(e.target.value) || 0)) })} />
        </FieldBox>
        <FieldBox field="friendship">
          <input type="number" min={0} max={255} className={inputClass} value={raw.friendship} onChange={(e) => set({ friendship: Math.max(0, Math.min(255, Number(e.target.value) || 0)) })} />
        </FieldBox>
      </div>

      <label className={`flex items-center gap-2 text-sm ${issues.fateful ? 'rounded-lg p-1 ring-2 ring-red-500' : ''}`}>
        <input type="checkbox" checked={raw.fateful} onChange={(e) => set({ fateful: e.target.checked })} /> {t('builder.fields.fateful')}
      </label>
    </div>
  )
}
