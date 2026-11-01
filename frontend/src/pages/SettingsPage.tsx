import { useEffect, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { Badge, Button, Callout, Field, HelpTip, Icon, Segmented, Toggle, inputClass, selectClass, useToast } from '../components/ui'
import { ApiError, api } from '../lib/api'
import { languages } from '../i18n'
import { familyOf, GAMES, gameByFamily, idLimits } from '../lib/games'
import { trainerNameErrors } from '../lib/trainer'
import type { GameFamily, MyTrainer, Pokemon, SetupStatus, TradeSettings } from '../lib/types'
import { useTheme, type ThemeChoice } from '../theme'
import { AppImageWorkerSettings } from './setup/AppImageWorker'
import { AccountsCard, ServerModeCard } from './settings/ServerSettings'
import { ConnectionChecklist } from './settings/ConnectionChecklist'
import { LogsTab } from './settings/LogsTab'
import { useAuth } from '../lib/auth'
import { useConnection } from '../lib/connection'
import { useConnectionSummary } from '../components/Layout'

type NumberSetting = Exclude<keyof TradeSettings, 'allowIllegal'>

const numberFields: { key: NumberSetting; min: number; max: number; unit: string }[] = [
  { key: 'maxAttempts', min: 1, max: 20, unit: 'ui.settings.unitTimes' },
  { key: 'retryDelaySeconds', min: 0, max: 60, unit: 'ui.settings.unitS' },
  { key: 'approvalTimeoutSeconds', min: 1, max: 900, unit: 'ui.settings.unitS' },
  { key: 'phaseTimeoutSeconds', min: 1, max: 1800, unit: 'ui.settings.unitS' },
  { key: 'totalTimeoutMinutes', min: 1, max: 180, unit: 'ui.settings.unitMin' },
]

type Tab = 'conexao' | 'treinador' | 'preferencias' | 'avancado' | 'logs' | 'contas'

export default function SettingsPage() {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const { me: caller } = useAuth()
  // Pela rede: status da conexão, "Meu treinador", preferências e (administradores) as contas.
  const local = caller?.local !== false
  const status = useConnectionSummary()
  const [settings, setSettings] = useState<TradeSettings | null>(null)

  useEffect(() => {
    if (local) api.get<TradeSettings>('/api/settings/trade').then(setSettings).catch(() => undefined)
  }, [local])

  const tabs: { key: Tab; label: string; icon: string; badge?: number }[] = [
    { key: 'conexao', label: t('ui.settings.tabs.conexao'), icon: 'cable', badge: status.missing || undefined },
    { key: 'treinador', label: t('settings.myTrainerTitle'), icon: 'badge' },
    { key: 'preferencias', label: t('ui.settings.tabs.preferencias'), icon: 'palette' },
    ...(local ? [{ key: 'avancado' as Tab, label: t('ui.settings.tabs.avancado'), icon: 'tune' }] : []),
    ...(local ? [{ key: 'logs' as Tab, label: t('logs.tab'), icon: 'receipt_long' }] : []),
    ...(!local && caller?.role === 'admin' ? [{ key: 'contas' as Tab, label: t('accounts.title'), icon: 'group' }] : []),
  ]
  const asked = params.get('aba') as Tab | null
  const tab: Tab = tabs.some((x) => x.key === asked) ? asked! : 'conexao'

  // Links antigos para o modo servidor (#servidor) caem na aba Avançado.
  useEffect(() => {
    if (window.location.hash === '#servidor') document.getElementById('servidor')?.scrollIntoView()
  }, [tab])

  async function saveSettings(next: TradeSettings) {
    const saved = await api.put<TradeSettings>('/api/settings/trade', next)
    setSettings(saved)
    return saved
  }

  return (
    <div className="flex flex-col gap-[18px]">
      <h1 className="font-display text-[23px] font-bold md:text-[28px]">{t('settings.title')}</h1>
      <div className="flex gap-1 overflow-x-auto border-b border-line" role="tablist">
        {tabs.map((x) => (
          <button
            key={x.key}
            type="button"
            role="tab"
            aria-selected={tab === x.key}
            onClick={() => setParams({ aba: x.key })}
            className={`-mb-px flex h-[42px] shrink-0 cursor-pointer items-center gap-1.5 border-b-[2.5px] px-3.5 text-[14.5px] font-bold whitespace-nowrap ${tab === x.key ? 'border-accent text-ink' : 'border-transparent text-muted'}`}
          >
            <Icon name={x.icon} className="text-[19px]" />
            {x.label}
            {x.badge && <span className="flex h-5 min-w-5 items-center justify-center rounded-full bg-bad px-1.5 text-[11.5px] text-white">{x.badge}</span>}
          </button>
        ))}
      </div>

      {tab === 'conexao' && <ConnectionChecklist />}
      {tab === 'treinador' && <TrainerTab />}
      {tab === 'preferencias' && <PreferencesTab settings={settings} onSave={saveSettings} />}
      {tab === 'avancado' && local && <AdvancedTab settings={settings} onSave={saveSettings} />}
      {tab === 'logs' && local && <LogsTab />}
      {tab === 'contas' && <AccountsCard />}
    </div>
  )
}

function Panel({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`flex flex-col gap-3.5 rounded-xl border border-line bg-surface px-[22px] py-5 ${className}`}>{children}</div>
}

function PanelTitle({ children, tip, sub, right }: { children: ReactNode; tip?: ReactNode; sub?: ReactNode; right?: ReactNode }) {
  return (
    <div className="flex items-center gap-3">
      <div className="flex-1">
        <div className="flex items-center gap-2">
          <div className="text-base font-extrabold">{children}</div>
          {tip && <HelpTip>{tip}</HelpTip>}
        </div>
        {sub && <div className="text-[13.5px] text-muted">{sub}</div>}
      </div>
      {right}
    </div>
  )
}

function TrainerTab() {
  const { t } = useTranslation()
  const toast = useToast()
  const empty: MyTrainer = { name: '', tid: 0, sid: null, gender: 0, badges: 8, hasNationalDex: true }
  // Um "Meu treinador" para cada jogo (decisão do autor, multijogos.md): os IDs e o nome mudam de formato.
  const [family, setFamily] = useState<GameFamily>('frlg')
  const game = gameByFamily(family)
  const limits = idLimits(game)
  const [me, setMe] = useState<MyTrainer>(empty)
  const [errors, setErrors] = useState<string[]>([])
  const [received, setReceived] = useState<Pokemon[]>([])
  const [pickOpen, setPickOpen] = useState(false)

  useEffect(() => {
    api.get<Pokemon[]>('/api/pokemon').then((list) => setReceived(list.filter((p) => p.origin === 'received'))).catch(() => undefined)
  }, [])

  useEffect(() => {
    setErrors([])
    setPickOpen(false)
    // Sem treinador nesse jogo: começa com o nome do FireRed/LeafGreen (o resto a pessoa preenche).
    void Promise.all([
      api.get<MyTrainer | undefined>(`/api/settings/my-trainer?game=${family}`),
      family === 'frlg' ? Promise.resolve(undefined) : api.get<MyTrainer | undefined>('/api/settings/my-trainer?game=frlg'),
    ])
      .then(([mine, frlg]) => setMe(mine ?? { ...empty, name: frlg?.name ?? '' }))
      .catch(() => setMe(empty))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [family])

  async function save() {
    const errs = trainerNameErrors(me.name, game)
    setErrors(errs)
    if (errs.length > 0) return
    try {
      setMe(await api.put<MyTrainer>(`/api/settings/my-trainer?game=${family}`, me))
      toast(t('common.saved'))
    } catch (e) {
      if (e instanceof ApiError) setErrors(e.errors.length > 0 ? e.errors : [e.code ?? 'unknown'])
    }
  }

  async function fromPokemon(id: string) {
    setMe(await api.post<MyTrainer>(`/api/settings/my-trainer/from-pokemon/${id}`))
    setPickOpen(false)
    toast(t('common.saved'))
  }

  const idValue = (v: string, max: number) => Math.max(0, Math.min(max, Number(v.replace(/\D/g, '')) || 0))

  return (
    <Panel className="max-w-[760px] gap-4">
      <PanelTitle tip={t('ui.settings.trainerTip')} sub={t('ui.settings.trainerSub')}>
        {t('settings.myTrainerTitle')}
      </PanelTitle>
      <Field label={t('vault.game')}>
        <select className={`${selectClass} max-w-xs`} value={family} onChange={(e) => setFamily(e.target.value as GameFamily)}>
          {GAMES.map((g) => (
            <option key={g.family} value={g.family}>
              {g.name}
            </option>
          ))}
        </select>
      </Field>
      <div className="grid gap-2.5 [grid-template-columns:repeat(auto-fit,minmax(140px,1fr))]">
        <Field label={t('trade.trainer.name')}>
          <input className={inputClass} maxLength={game.trainerNameMax} placeholder={t('ui.settings.nameHint')} value={me.name} onChange={(e) => setMe({ ...me, name: e.target.value })} />
        </Field>
        <Field label={t('trade.trainer.tid')}>
          <input className={inputClass} inputMode="numeric" value={me.tid} onChange={(e) => setMe({ ...me, tid: idValue(e.target.value, limits.tid) })} />
        </Field>
        <Field label={t('trade.trainer.sid')}>
          <input
            className={inputClass}
            inputMode="numeric"
            placeholder={t('common.optional')}
            value={me.sid ?? ''}
            onChange={(e) => setMe({ ...me, sid: e.target.value.trim() === '' ? null : idValue(e.target.value, limits.sid) })}
          />
        </Field>
        <Field label={t('ui.settings.gender')}>
          <select className={selectClass} value={me.gender} onChange={(e) => setMe({ ...me, gender: Number(e.target.value) })}>
            <option value={0}>♂</option>
            <option value={1}>♀</option>
          </select>
        </Field>
      </div>
      {family === 'frlg' && (
        <div className="flex flex-wrap items-center gap-6">
          <div className="flex items-center gap-2.5">
            <span className="text-sm font-bold">{t('settings.badges')}</span>
            <div className="flex gap-1">
              {Array.from({ length: 8 }, (_, i) => (
                <button
                  key={i}
                  type="button"
                  aria-label={t('ui.settings.badgeN', { n: i + 1 })}
                  onClick={() => setMe({ ...me, badges: me.badges === i + 1 ? i : i + 1 })}
                  className={`h-[22px] w-[22px] cursor-pointer rounded-full border-[1.5px] ${i < me.badges ? 'border-accent bg-accent' : 'border-line bg-transparent'}`}
                />
              ))}
            </div>
            <span className="text-[13px] text-muted">{me.badges}/8</span>
          </div>
          <Toggle small on={me.hasNationalDex} onChange={(on) => setMe({ ...me, hasNationalDex: on })} label={t('settings.nationalDex')} />
        </div>
      )}
      {errors.length > 0 && (
        <ul className="text-sm text-bad">
          {errors.map((e) => (
            <li key={e}>{t(`errors.${e}`)}</li>
          ))}
        </ul>
      )}
      <div className="flex flex-wrap gap-2">
        <Button onClick={() => void save()}>{t('common.save')}</Button>
        <Button variant="secondary" icon="download" disabled={!received.some((p) => familyOf(p) === family)} onClick={() => setPickOpen(!pickOpen)}>
          {t('settings.fromPokemon')}
        </Button>
      </div>
      {pickOpen && (
        <div className="flex flex-col gap-1">
          <p className="text-sm text-muted">{t('settings.choosePokemon')}</p>
          {received.filter((p) => familyOf(p) === family).map((p) => (
            <button key={p.id} type="button" className="cursor-pointer rounded-lg px-3 py-2 text-left text-sm hover:bg-surface2" onClick={() => void fromPokemon(p.id)}>
              {p.nickname || p.speciesName} · {p.otName} · {p.tid}:{p.sid}
            </button>
          ))}
        </div>
      )}
    </Panel>
  )
}

function PreferencesTab({ settings, onSave }: { settings: TradeSettings | null; onSave: (s: TradeSettings) => Promise<TradeSettings> }) {
  const { t, i18n } = useTranslation()
  const [theme, setTheme] = useTheme()
  const toast = useToast()
  const row = 'flex flex-wrap items-center gap-3.5 py-4'
  return (
    <div className="flex max-w-[760px] flex-col rounded-xl border border-line bg-surface px-[22px] py-1.5">
      <div className={`${row} border-b border-line`}>
        <div className="flex-1">
          <div className="font-bold">{t('settings.theme')}</div>
          <div className="text-[13px] text-muted">{t('ui.settings.themeSub')}</div>
        </div>
        <Segmented<ThemeChoice>
          value={theme}
          onChange={setTheme}
          options={[
            { value: 'system', label: t('settings.themeSystem') },
            { value: 'light', label: t('settings.themeLight') },
            { value: 'dark', label: t('settings.themeDark') },
          ]}
        />
      </div>
      <div className={`${row} ${settings ? 'border-b border-line' : ''}`}>
        <div className="flex-1 font-bold">{t('settings.language')}</div>
        <select className={`${selectClass} h-10! w-auto!`} value={i18n.language} onChange={(e) => void i18n.changeLanguage(e.target.value)}>
          {languages.map((l) => (
            <option key={l.code} value={l.code}>
              {l.label}
            </option>
          ))}
        </select>
      </div>
      {settings && (
        <div className={row}>
          <div className="flex-1">
            <div className="flex items-center gap-2">
              <div className="font-bold">{t('ui.settings.allowIllegal')}</div>
              <HelpTip>{t('ui.settings.illegalTip')}</HelpTip>
            </div>
            <div className="text-[13px] text-muted">{t('ui.settings.illegalSub')}</div>
          </div>
          <Toggle
            on={settings.allowIllegal}
            onChange={(on) => void onSave({ ...settings, allowIllegal: on }).then(() => toast(t('common.saved')))}
          />
        </div>
      )}
    </div>
  )
}

function AdvancedTab({ settings, onSave }: { settings: TradeSettings | null; onSave: (s: TradeSettings) => Promise<TradeSettings> }) {
  const { t } = useTranslation()
  const toast = useToast()
  const { engines, reloadEngines, setSetup, reload } = useConnection()
  const [draft, setDraft] = useState<TradeSettings | null>(settings)
  const [engineError, setEngineError] = useState<string | null>(null)
  const [scenarios, setScenarios] = useState<{ scenarios: string[]; current: string | null } | null>(null)
  const [confirmReset, setConfirmReset] = useState(false)
  const isAdmin = useAuth().me?.role === 'admin'

  useEffect(() => setDraft(settings), [settings])
  useEffect(() => {
    // Só existe em desenvolvimento (404 no app final).
    api.get<{ scenarios: string[]; current: string | null }>('/api/setup/scenarios').then(setScenarios).catch(() => undefined)
  }, [])

  async function selectEngine(id: string) {
    try {
      await api.put('/api/engines/selected', { id })
      setEngineError(null)
      await reloadEngines()
    } catch (e) {
      setEngineError(e instanceof ApiError && e.code ? e.code : 'unknown')
    }
  }

  async function restart() {
    setSetup(await api.post<SetupStatus>('/api/setup/reset'))
    setConfirmReset(false)
    toast(t('ui.settings.restarted'))
  }

  return (
    <div className="flex max-w-[860px] flex-col gap-3.5">
      <Callout tone="sky" icon="info">
        {t('ui.settings.defaultsOk')}
      </Callout>

      {draft && (
        <Panel>
          <PanelTitle tip={t('ui.settings.tempoTip')}>{t('ui.settings.tempos')}</PanelTitle>
          <div className="grid gap-3 [grid-template-columns:repeat(auto-fill,minmax(220px,1fr))]">
            {numberFields.map((f) => (
              <label key={f.key} className="flex flex-col gap-1.5">
                <span className="text-[13.5px] font-semibold">{t(`ui.settings.fields.${f.key}`)}</span>
                <span className="flex h-[42px] items-center rounded-lg border border-line bg-bg px-3 focus-within:border-accent">
                  <input
                    inputMode="numeric"
                    className="min-w-0 flex-1 bg-transparent font-bold outline-none"
                    value={draft[f.key]}
                    onChange={(e) => setDraft({ ...draft, [f.key]: Math.min(f.max, Number(e.target.value.replace(/\D/g, '')) || 0) })}
                  />
                  <span className="text-[12.5px] text-muted">{t(f.unit)}</span>
                </span>
              </label>
            ))}
          </div>
          <div className="flex gap-2">
            <Button
              onClick={() =>
                void onSave({
                  ...draft,
                  ...Object.fromEntries(numberFields.map((f) => [f.key, Math.max(f.min, draft[f.key])])),
                }).then(() => toast(t('common.saved')))
              }
            >
              {t('common.save')}
            </Button>
            <Button variant="secondary" onClick={() => settings && setDraft(settings)}>
              {t('ui.settings.undo')}
            </Button>
          </div>
        </Panel>
      )}

      <Panel>
        <PanelTitle tip={t('ui.settings.engineTip')} sub={t('settings.engineHelp')}>
          {t('settings.engineTitle')}
        </PanelTitle>
        <div className="flex flex-col gap-1.5">
          {engines.map((engine) => (
            <button
              key={engine.id}
              type="button"
              disabled={!engine.available}
              onClick={() => void selectEngine(engine.id)}
              aria-pressed={engine.selected}
              className={`flex min-h-12 cursor-pointer flex-wrap items-center gap-2.5 rounded-lg border-[1.5px] bg-bg px-3 py-2 text-left disabled:cursor-not-allowed disabled:opacity-55 ${engine.selected ? 'border-accent' : 'border-line'}`}
            >
              <Icon name={engine.selected ? 'radio_button_checked' : 'radio_button_unchecked'} className={engine.selected ? 'text-accent' : 'text-muted'} />
              <span className="flex-1 text-sm font-semibold">{t(`settings.engines.${engine.id}`, { defaultValue: engine.id })}</span>
              {engine.isSimulation && <Badge tone="amber">{t('demo.badge')}</Badge>}
              {!engine.available && engine.reasonCode && (
                <span className="text-xs text-muted">{t(`settings.engineReasons.${engine.reasonCode}`, { defaultValue: engine.reasonCode })}</span>
              )}
            </button>
          ))}
        </div>
        {engineError && (
          <p role="alert" className="text-sm text-bad">
            {t(`settings.engineErrors.${engineError}`, { defaultValue: t('common.error') })}
          </p>
        )}
        <AppImageWorkerSettings />
      </Panel>

      <ServerModeCard />
      {isAdmin && <AccountsCard />}

      <Panel>
        <div className="flex flex-wrap items-center gap-3.5">
          <div className="min-w-[220px] flex-1">
            <div className="flex items-center gap-2">
              <div className="text-base font-extrabold">{t('ui.settings.dev')}</div>
              <HelpTip>{t('ui.settings.devTip')}</HelpTip>
            </div>
            <div className="text-[13.5px] text-muted">{scenarios ? t('ui.settings.devSub') : t('ui.settings.restartSub')}</div>
          </div>
          {scenarios && (
            <select
              className={`${selectClass} h-10! w-auto!`}
              value={scenarios.current ?? ''}
              onChange={async (e) => {
                const scenario = e.target.value || null
                setSetup(await api.put<SetupStatus>('/api/setup/simulation', { scenario }))
                setScenarios({ ...scenarios, current: scenario })
                void reload()
              }}
            >
              <option value="">{t('wizard.simulationNone')}</option>
              {scenarios.scenarios.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          )}
          {confirmReset ? (
            <>
              <Button variant="danger" onClick={() => void restart()}>
                {t('ui.settings.restartConfirm')}
              </Button>
              <Button variant="ghost" onClick={() => setConfirmReset(false)}>
                {t('common.cancel')}
              </Button>
            </>
          ) : (
            <Button variant="secondary" onClick={() => setConfirmReset(true)}>
              {t('wizard.restart')}
            </Button>
          )}
        </div>
      </Panel>
    </div>
  )
}
