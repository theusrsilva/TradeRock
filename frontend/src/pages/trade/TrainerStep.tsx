import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Badge, Button, Field, HelpTip, Icon, Toggle, inputClass, selectClass } from '../../components/ui'
import { api } from '../../lib/api'
import { gameOf, idLimits, LGPE_CODE_SPECIES, linkCodeErrors, versionColor, versionName } from '../../lib/games'
import { spriteUrl } from '../../lib/api'
import { trainerNameErrors } from '../../lib/trainer'
import type { SimulationScenario, TradeGame, Trainer, TrainerPreset } from '../../lib/types'

export interface TrainerState {
  game: TradeGame
  trainer: Trainer
  scenario: SimulationScenario
  failuresBeforeSuccess: number
  /** Oito dígitos ou, no Let's Go, três índices da grade ("0,4,9"). Vazio = sem código. */
  linkCode: string
  /** Depois de cada troca, devolve ao Switch o Pokémon recebido para ele evoluir lá (só nos motores com o pokeldn). */
  tradeBack: boolean
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

const parseId = (value: string, max: number): number | null => {
  if (value.trim() === '') return null
  const n = Number(value)
  return Number.isInteger(n) && n >= 0 && n <= max ? n : null
}

export function TrainerStep({
  state,
  onChange,
  simulation,
  canTradeBack,
  linkCodeIgnored = false,
}: {
  state: TrainerState
  onChange: (state: TrainerState) => void
  simulation: boolean
  canTradeBack: boolean
  /** Modo LAN do Sword/Shield: o jogo ignora o código (D24). */
  linkCodeIgnored?: boolean
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

  const game = gameOf(state.game)
  const limits = idLimits(game)
  const nameErrors = trainerNameErrors(state.trainer.name, game)
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

  // Só os treinadores salvos do mesmo jogo (os números mudam de formato entre FRLG e os jogos novos).
  const samePresets = presets.filter((p) => gameOf(p.game).family === game.family)
  const isCurrent = (p: TrainerPreset) =>
    p.name === state.trainer.name && p.tid === state.trainer.tid && p.sid === state.trainer.sid && p.game === state.game

  return (
    <div className="grid items-start gap-[18px] [grid-template-columns:repeat(auto-fit,minmax(min(320px,100%),1fr))]">
      <div className="flex flex-col gap-[18px] rounded-xl border border-line bg-surface p-[22px]">
        <div className="flex items-center gap-2 text-sm text-muted">
          <span className="h-3 w-3 rounded-full" style={{ background: versionColor(state.game) }} />
          {t('ui.trade.tradingWith', { game: versionName(state.game) })}
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
              maxLength={game.trainerNameMax + 3}
              onChange={(e) => setTrainer({ name: e.target.value })}
              aria-invalid={nameErrors.length > 0}
            />
            <span className="text-[13px] text-muted">
              {[...state.trainer.name].length}/{game.trainerNameMax}
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
                const n = parseId(e.target.value, limits.tid)
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
                  setTrainer({ sid: parseId(e.target.value, limits.sid) })
                }}
              />
            </Field>
          )}
        </div>
        <button type="button" onClick={() => setMore(!more)} className="flex cursor-pointer items-center gap-1 self-start font-semibold text-accent">
          <Icon name={more ? 'expand_less' : 'expand_more'} />
          {more ? t('ui.trade.lessOptions') : t('ui.trade.moreOptions')}
        </button>

        {game.linkCode !== 'none' && !linkCodeIgnored && (
          <div className="flex flex-col gap-1.5">
            <div className="flex items-center gap-1.5">
              <span className="flex-1 font-bold">
                {t('ui.trade.linkCode')} <span className="font-normal text-muted">({t('common.optional')})</span>
              </span>
              <HelpTip>{t(game.linkCode === 'digits8' ? 'ui.trade.linkCodeTip' : 'ui.trade.linkCodeLgpeTip')}</HelpTip>
            </div>
            {game.linkCode === 'digits8' ? (
              <input
                className={`${inputClass} font-mono tracking-[3px]`}
                inputMode="numeric"
                maxLength={8}
                placeholder="12345678"
                value={state.linkCode}
                onChange={(e) => onChange({ ...state, linkCode: e.target.value.replace(/\D/g, '').slice(0, 8) })}
              />
            ) : (
              <LgpeCode value={state.linkCode} onChange={(linkCode) => onChange({ ...state, linkCode })} />
            )}
            {linkCodeErrors(state.game, state.linkCode).map((e) => (
              <div key={e} className="text-[13px] text-bad">
                {t(`errors.${e}`)}
              </div>
            ))}
          </div>
        )}

        {canTradeBack && (
          <div className="flex items-center gap-1.5">
            <Toggle small on={state.tradeBack} onChange={(tradeBack) => onChange({ ...state, tradeBack })} label={t('ui.trade.tradeBack')} />
            <HelpTip>{t('ui.trade.tradeBackTip')}</HelpTip>
          </div>
        )}
      </div>

      <div className="flex flex-col gap-[18px]">
        <div className="flex flex-col gap-3 rounded-xl border border-line bg-surface p-5">
          <div className="font-bold">{t('trade.trainer.presets')}</div>
          {samePresets.length === 0 && <p className="text-sm text-muted">{t('trade.trainer.noPresets')}</p>}
          <div className="flex flex-wrap gap-2">
            {samePresets.map((p) => (
              <span
                key={p.id}
                className={`flex h-9 items-center gap-1 rounded-full border pr-1 pl-3.5 text-[13.5px] font-semibold ${isCurrent(p) ? 'border-accent bg-accent-soft' : 'border-line bg-surface'}`}
              >
                <button type="button" onClick={() => applyPreset(p)} className="flex cursor-pointer items-center gap-2" title={`${p.name} · ${p.tid}${p.sid !== null ? `:${p.sid}` : ''}`}>
                  <span className="h-2.5 w-2.5 rounded-full" style={{ background: versionColor(p.game) }} />
                  {p.label} · {versionName(p.game)}
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

/** Código do Let's Go: três Pokémon escolhidos, em ordem, na grade de dez. */
function LgpeCode({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const { t } = useTranslation()
  const picks = value === '' ? [] : value.split(',').map(Number)
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        {[0, 1, 2].map((i) => (
          <span key={i} className="flex h-12 w-12 items-center justify-center rounded-lg border border-line bg-bg">
            {picks[i] !== undefined && <img src={spriteUrl(LGPE_CODE_SPECIES[picks[i]])} alt="" className="pixelated h-11 w-11" />}
          </span>
        ))}
        {picks.length > 0 && (
          <Button variant="ghost" onClick={() => onChange('')}>
            {t('common.clear')}
          </Button>
        )}
      </div>
      <div className="grid grid-cols-5 gap-1.5">
        {LGPE_CODE_SPECIES.map((species, index) => (
          <button
            key={species}
            type="button"
            disabled={picks.length >= 3}
            onClick={() => onChange([...picks, index].join(','))}
            className="flex cursor-pointer items-center justify-center rounded-lg border border-line bg-tile p-1 disabled:cursor-not-allowed disabled:opacity-50"
          >
            <img src={spriteUrl(species)} alt="" className="pixelated h-10 w-10" />
          </button>
        ))}
      </div>
    </div>
  )
}
