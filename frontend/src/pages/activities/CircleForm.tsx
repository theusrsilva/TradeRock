import { useTranslation } from 'react-i18next'
import { Field, inputClass, Segmented, selectClass, Toggle } from '../../components/ui'
import { useBuilderData } from '../../lib/builderData'

/** Union Circle do Scarlet/Violet (D26): o console abre o círculo e o TradeRock entra. */
export interface CircleState {
  kind: 'quests' | 'encounters'
  code: string
  task: 'snacksworth' | 'bp'
  bp: number
  species?: number
  level: number
  shiny: boolean
  tera?: number
}

export const newCircle = (): CircleState => ({ kind: 'quests', code: '', task: 'snacksworth', bp: 1000, level: 30, shiny: false })

export function circleReady(c: CircleState): boolean {
  if (!/^\d{4}$/.test(c.code)) return false
  if (c.kind === 'quests') return c.task === 'snacksworth' || (c.bp >= 1 && c.bp <= 9_999_999)
  return !!c.species && c.level >= 1 && c.level <= 100
}

// Ordem dos tipos do jogo (--spawn-tera 0 Normal a 17 Fairy).
const TYPES = ['Normal', 'Fighting', 'Flying', 'Poison', 'Ground', 'Rock', 'Bug', 'Ghost', 'Steel', 'Fire', 'Water', 'Grass',
  'Electric', 'Psychic', 'Ice', 'Dragon', 'Dark', 'Fairy']

export function CircleForm({ circle, onChange }: { circle: CircleState; onChange: (c: CircleState) => void }) {
  const { t } = useTranslation()
  const data = useBuilderData('scarlet')
  const set = (patch: Partial<CircleState>) => onChange({ ...circle, ...patch })

  return (
    <div className="flex flex-col gap-4">
      <Segmented<CircleState['kind']>
        className="self-start"
        value={circle.kind}
        onChange={(kind) => set({ kind })}
        options={[
          { value: 'quests', label: t('activities.circle.kinds.quests') },
          { value: 'encounters', label: t('activities.circle.kinds.encounters') },
        ]}
      />
      <p className="text-sm text-muted">{t(`activities.circle.about.${circle.kind}`)}</p>
      <Field label={t('activities.circle.code')} help={t('activities.circle.codeHelp')}>
        <input
          className={inputClass}
          inputMode="numeric"
          maxLength={4}
          value={circle.code}
          onChange={(e) => set({ code: e.target.value.replace(/\D/g, '') })}
        />
      </Field>

      {circle.kind === 'quests' && (
        <>
          <Field label={t('activities.circle.task')}>
            <select className={selectClass} value={circle.task} onChange={(e) => set({ task: e.target.value as CircleState['task'] })}>
              <option value="snacksworth">{t('activities.circle.tasks.snacksworth')}</option>
              <option value="bp">{t('activities.circle.tasks.bp')}</option>
            </select>
          </Field>
          {circle.task === 'bp' && (
            <Field label={t('activities.circle.bp')} help={t('activities.circle.bpHelp')}>
              <input
                className={inputClass}
                inputMode="numeric"
                value={circle.bp}
                onChange={(e) => set({ bp: Math.min(9_999_999, Number(e.target.value.replace(/\D/g, '')) || 0) })}
              />
            </Field>
          )}
        </>
      )}

      {circle.kind === 'encounters' && (
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label={t('activities.circle.species')}>
            <select className={selectClass} value={circle.species ?? ''} onChange={(e) => set({ species: Number(e.target.value) || undefined })}>
              <option value="">{t('activities.circle.chooseSpecies')}</option>
              {data?.species.map((s) => (
                <option key={s.value} value={s.value}>
                  {s.text}
                </option>
              ))}
            </select>
          </Field>
          <Field label={t('activities.circle.level')}>
            <input
              className={inputClass}
              inputMode="numeric"
              value={circle.level}
              onChange={(e) => set({ level: Math.min(100, Number(e.target.value.replace(/\D/g, '')) || 0) })}
            />
          </Field>
          <Field label={t('activities.circle.tera')}>
            <select className={selectClass} value={circle.tera ?? ''} onChange={(e) => set({ tera: e.target.value === '' ? undefined : Number(e.target.value) })}>
              <option value="">{t('activities.circle.teraDefault')}</option>
              {TYPES.map((name, i) => (
                <option key={name} value={i}>
                  {name}
                </option>
              ))}
            </select>
          </Field>
          <div className="flex items-end pb-2">
            <Toggle on={circle.shiny} onChange={(shiny) => set({ shiny })} label={t('activities.circle.shiny')} />
          </div>
        </div>
      )}
    </div>
  )
}
