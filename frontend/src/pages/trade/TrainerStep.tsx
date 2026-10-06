import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Badge, Button, Field, HelpTip, Icon, inputClass, selectClass } from '../../components/ui'
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

export const gameDot: Record<TradeGame, string> = { fire_red: '#d8443a', leaf_green: '#3f9a4e' }

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
  const [label, setLabel] = useState<string | null>(null)
  const [tidText, setTidText] = useState(String(state.trainer.tid))
  const [sidText, setSidText] = useState(state.trainer.sid === null ? '' : String(state.trainer.sid))
  const [more, setMore] = useState(false)

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
    if (!label?.trim()) return
    await api.post('/api/trainer-presets', { label: label.trim(), ...state.trainer, game: state.game })
    setLabel(null)
    await loadPresets()
  }

  const isCurrent = (p: TrainerPreset) =>
    p.name === state.trainer.name && p.tid === state.trainer.tid && p.sid === state.trainer.sid && p.game === state.game

  return (
    <div className="grid items-start gap-[18px] [grid-template-columns:repeat(auto-fit,minmax(min(320px,100%),1fr))]">
      <div className="flex flex-col gap-[18px] rounded-xl border border-line bg-surface p-[22px]">
        <div className="flex flex-col gap-2">
          <div className="font-bold">{t('ui.trade.whichGame')}</div>
          <div className="grid gap-2.5 [grid-template-columns:repeat(auto-fit,minmax(min(140px,100%),1fr))]" role="radiogroup">
            {(['fire_red', 'leaf_green'] as const).map((game) => {
              const on = state.game === game
              return (
                <button
                  key={game}
                  type="button"
                  role="radio"
                  aria-checked={on}
                  onClick={() => onChange({ ...state, game })}
                  className={`flex h-16 cursor-pointer items-center gap-3 rounded-lg border-2 px-4 text-left ${on ? 'border-accent bg-accent-soft' : 'border-line bg-bg'}`}
                >
                  <span className="h-[18px] w-[18px] rounded-full" style={{ background: gameDot[game] }} />
                  <span className="flex-1 text-base font-bold">{t(`trade.trainer.${game}`)}</span>
                  {on && <Icon name="check_circle" className="text-[22px] text-accent" />}
                </button>
              )
            })}
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <div className="flex items-center gap-1.5">
            <label htmlFor="trainer-name" className="flex-1 font-bold">
              {t('ui.trade.nameOnSwitch')}
            </label>
            <HelpTip>{t('ui.trade.nameTip')}</HelpTip>
          </div>
          <div className={`flex h-[46px] items-center rounded-lg border-[1.5px] bg-bg px-3.5 ${nameErrors.length > 0 ? 'border-bad' : 'border-line focus-within:border-accent'}`}>
            <input
              id="trainer-name"
              className="min-w-0 flex-1 bg-transparent text-[17px] font-bold tracking-[1px] outline-none"
              value={state.trainer.name}
              maxLength={TRAINER_NAME_MAX + 3}
              onChange={(e) => setTrainer({ name: e.target.value })}
              aria-invalid={nameErrors.length > 0}
            />
            <span className="text-[13px] text-muted">
              {[...state.trainer.name].length}/{TRAINER_NAME_MAX}
            </span>
          </div>
          {nameErrors.map((e) => (
            <div key={e} className="text-[13px] text-bad">
              {t(`errors.${e}`)}
            </div>
          ))}
        </div>

        <div className="grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(min(140px,100%),1fr))]">
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
          {more && (
            <Field label={<>{t('trade.trainer.sid')} <span className="font-normal text-muted">({t('common.optional')})</span></>}>
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
          )}
        </div>
        <button type="button" onClick={() => setMore(!more)} className="flex cursor-pointer items-center gap-1 self-start font-semibold text-accent">
          <Icon name={more ? 'expand_less' : 'expand_more'} />
          {more ? t('ui.trade.lessOptions') : t('ui.trade.moreOptions')}
        </button>
      </div>

      <div className="flex flex-col gap-[18px]">
        <div className="flex flex-col gap-3 rounded-xl border border-line bg-surface p-5">
          <div className="font-bold">{t('trade.trainer.presets')}</div>
          {presets.length === 0 && <p className="text-sm text-muted">{t('trade.trainer.noPresets')}</p>}
          <div className="flex flex-wrap gap-2">
            {presets.map((p) => (
              <span
                key={p.id}
                className={`flex h-9 items-center gap-1 rounded-full border pr-1 pl-3.5 text-[13.5px] font-semibold ${isCurrent(p) ? 'border-accent bg-accent-soft' : 'border-line bg-surface'}`}
              >
                <button type="button" onClick={() => applyPreset(p)} className="flex cursor-pointer items-center gap-2" title={`${p.name} · ${p.tid}${p.sid !== null ? `:${p.sid}` : ''}`}>
                  <span className="h-2.5 w-2.5 rounded-full" style={{ background: gameDot[p.game] }} />
                  {p.label} · {t(`trade.trainer.${p.game}`)}
                </button>
                <button
                  type="button"
                  aria-label={t('common.delete')}
                  title={t('common.delete')}
                  onClick={() => void api.delete(`/api/trainer-presets/${p.id}`).then(loadPresets)}
                  className="flex h-7 w-7 cursor-pointer items-center justify-center rounded-full text-muted hover:bg-surface2"
                >
                  <Icon name="close" className="text-base" />
                </button>
              </span>
            ))}
          </div>
          {label === null ? (
            <button
              type="button"
              disabled={nameErrors.length > 0}
              onClick={() => setLabel(state.trainer.name)}
              className="flex h-[34px] cursor-pointer items-center gap-1.5 self-start rounded-lg border border-dashed border-line px-3 text-[13px] font-semibold text-muted disabled:cursor-not-allowed disabled:opacity-50"
            >
              <Icon name="bookmark_add" className="text-[18px]" />
              {t('ui.trade.saveTrainer')}
            </button>
          ) : (
            <div className="flex flex-wrap gap-2">
              <input
                autoFocus
                className={`${inputClass} h-10! flex-1`}
                placeholder={t('trade.trainer.presetLabel')}
                value={label}
                maxLength={40}
                onChange={(e) => setLabel(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && void savePreset()}
              />
              <Button disabled={!label.trim()} onClick={() => void savePreset()}>
                {t('common.save')}
              </Button>
              <Button variant="ghost" onClick={() => setLabel(null)}>
                {t('common.cancel')}
              </Button>
            </div>
          )}
        </div>

        {simulation && (
          <div className="flex flex-col gap-2.5 rounded-xl border border-line bg-surface p-5">
            <div className="flex items-center gap-2 font-bold">
              <Icon name="science" className="text-warn" />
              {t('trade.simulation.title')}
              <Badge tone="amber">{t('demo.badge')}</Badge>
            </div>
            <div className="text-[13.5px] text-muted">{t('trade.simulation.help')}</div>
            <select
              className={selectClass}
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
          </div>
        )}
      </div>
    </div>
  )
}
