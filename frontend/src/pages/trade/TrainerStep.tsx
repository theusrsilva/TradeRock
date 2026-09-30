import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ContentFile } from '../../components/Markdown'
import { Badge, Button, Card, Field, inputClass } from '../../components/ui'
import { api } from '../../lib/api'
import { TRAINER_NAME_MAX, trainerNameErrors } from '../../lib/trainer'
import type { SimulationScenario, TradeGame, Trainer, TrainerPreset } from '../../lib/types'

export interface TrainerState {
  game: TradeGame
  trainer: Trainer
  scenario: SimulationScenario
  failuresBeforeSuccess: number
}

const scenarios: SimulationScenario[] = [
  'success',
  'connect_failures_then_success',
  'no_network_found',
  'wrong_keys',
  'approval_never_given',
  'join_rejected',
  'host_cancels',
  'connection_lost_mid_trade',
]

const parseId = (value: string): number | null => {
  if (value.trim() === '') return null
  const n = Number(value)
  return Number.isInteger(n) && n >= 0 && n <= 65535 ? n : null
}

export function TrainerStep({
  state,
  onChange,
  simulation,
}: {
  state: TrainerState
  onChange: (state: TrainerState) => void
  simulation: boolean
}) {
  const { t } = useTranslation()
  const [presets, setPresets] = useState<TrainerPreset[]>([])
  const [label, setLabel] = useState('')
  const [tidText, setTidText] = useState(String(state.trainer.tid))
  const [sidText, setSidText] = useState(state.trainer.sid === null ? '' : String(state.trainer.sid))

  const loadPresets = () => api.get<TrainerPreset[]>('/api/trainer-presets').then(setPresets).catch(() => undefined)
  useEffect(() => {
    void loadPresets()
  }, [])

  const nameErrors = trainerNameErrors(state.trainer.name)
  const setTrainer = (trainer: Partial<Trainer>) => onChange({ ...state, trainer: { ...state.trainer, ...trainer } })

  function applyPreset(p: TrainerPreset) {
    onChange({ ...state, game: p.game, trainer: { name: p.name, tid: p.tid, sid: p.sid } })
    setTidText(String(p.tid))
    setSidText(p.sid === null ? '' : String(p.sid))
  }

  async function savePreset() {
    await api.post('/api/trainer-presets', { label, ...state.trainer, game: state.game })
    setLabel('')
    await loadPresets()
  }

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Card className="space-y-4">
        <h2 className="font-semibold">{t('trade.trainer.title')}</h2>
        <ContentFile name="trade/treinador" />

        <fieldset className="space-y-2">
          <legend className="text-sm font-medium">{t('trade.trainer.game')}</legend>
          <div className="flex gap-2">
            {(['fire_red', 'leaf_green'] as const).map((game) => (
              <label
                key={game}
                className={`flex-1 cursor-pointer rounded-lg border px-3 py-2 text-center text-sm font-medium ${
                  state.game === game
                    ? game === 'fire_red'
                      ? 'border-red-500 bg-red-50 text-red-800 dark:bg-red-950/40 dark:text-red-300'
                      : 'border-emerald-500 bg-emerald-50 text-emerald-800 dark:bg-emerald-950/40 dark:text-emerald-300'
                    : 'border-slate-300 dark:border-slate-600'
                }`}
              >
                <input type="radio" name="game" className="sr-only" checked={state.game === game} onChange={() => onChange({ ...state, game })} />
                {t(`trade.trainer.${game}`)}
              </label>
            ))}
          </div>
        </fieldset>

        <Field label={t('trade.trainer.name')} help={t('trade.trainer.nameHelp')}>
          <input
            className={inputClass}
            value={state.trainer.name}
            maxLength={TRAINER_NAME_MAX + 3}
            onChange={(e) => setTrainer({ name: e.target.value })}
            aria-invalid={nameErrors.length > 0}
          />
        </Field>
        {nameErrors.length > 0 && (
          <ul className="text-sm text-red-700 dark:text-red-400">
            {nameErrors.map((e) => (
              <li key={e}>{t(`errors.${e}`)}</li>
            ))}
          </ul>
        )}

        <div className="grid grid-cols-2 gap-3">
          <Field label={t('trade.trainer.tid')}>
            <input
              className={inputClass}
              inputMode="numeric"
              value={tidText}
              onChange={(e) => {
                setTidText(e.target.value)
                const n = parseId(e.target.value)
                if (n !== null) setTrainer({ tid: n })
              }}
            />
          </Field>
          <Field label={`${t('trade.trainer.sid')} (${t('common.optional')})`}>
            <input
              className={inputClass}
              inputMode="numeric"
              value={sidText}
              onChange={(e) => {
                setSidText(e.target.value)
                setTrainer({ sid: parseId(e.target.value) })
              }}
            />
          </Field>
        </div>
      </Card>

      <div className="space-y-4">
        <Card className="space-y-3">
          <h2 className="font-semibold">{t('trade.trainer.presets')}</h2>
          {presets.length === 0 && <p className="text-sm text-slate-500">{t('trade.trainer.noPresets')}</p>}
          <ul className="space-y-2">
            {presets.map((p) => (
              <li key={p.id} className="flex items-center justify-between gap-2 text-sm">
                <span>
                  <strong>{p.label}</strong> · {p.name} · {p.tid}
                  {p.sid !== null && `:${p.sid}`} · {t(`trade.trainer.${p.game}`)}
                </span>
                <span className="flex gap-1">
                  <Button variant="secondary" onClick={() => applyPreset(p)}>
                    {t('trade.trainer.use')}
                  </Button>
                  <Button variant="ghost" onClick={() => void api.delete(`/api/trainer-presets/${p.id}`).then(loadPresets)}>
                    {t('common.delete')}
                  </Button>
                </span>
              </li>
            ))}
          </ul>
          <div className="flex gap-2">
            <input className={inputClass} placeholder={t('trade.trainer.presetLabel')} value={label} maxLength={40} onChange={(e) => setLabel(e.target.value)} />
            <Button variant="secondary" disabled={!label.trim() || nameErrors.length > 0} onClick={() => void savePreset()}>
              {t('trade.trainer.savePreset')}
            </Button>
          </div>
        </Card>

        {simulation && (
          <Card className="space-y-3">
            <div className="flex items-center gap-2">
              <h2 className="font-semibold">{t('trade.simulation.title')}</h2>
              <Badge tone="amber">{t('demo.badge')}</Badge>
            </div>
            <p className="text-sm text-slate-500">{t('trade.simulation.help')}</p>
            <select
              className={inputClass}
              value={state.scenario}
              onChange={(e) => onChange({ ...state, scenario: e.target.value as SimulationScenario })}
            >
              {scenarios.map((s) => (
                <option key={s} value={s}>
                  {t(`trade.simulation.scenario.${s}`)}
                </option>
              ))}
            </select>
            {state.scenario === 'connect_failures_then_success' && (
              <Field label={t('trade.simulation.failures')}>
                <input
                  type="number"
                  min={1}
                  max={10}
                  className={inputClass}
                  value={state.failuresBeforeSuccess}
                  onChange={(e) => onChange({ ...state, failuresBeforeSuccess: Math.max(1, Math.min(10, Number(e.target.value) || 1)) })}
                />
              </Field>
            )}
          </Card>
        )}
      </div>
    </div>
  )
}
