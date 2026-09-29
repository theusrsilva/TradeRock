import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Card, Field, inputClass } from '../components/ui'
import { api } from '../lib/api'
import { languages } from '../i18n'
import { ContentFile } from '../components/Markdown'
import { trainerNameErrors } from '../lib/trainer'
import type { Engine, MyTrainer, Pokemon, TradeSettings } from '../lib/types'
import { Link } from 'react-router'
import { useTheme, type ThemeChoice } from '../theme'

type NumberSetting = Exclude<keyof TradeSettings, 'allowIllegal'>

const numberFields: { key: NumberSetting; min: number; max: number; step?: number; help?: string }[] = [
  { key: 'maxAttempts', min: 1, max: 20, help: 'settings.maxAttemptsHelp' },
  { key: 'retryDelaySeconds', min: 0, max: 60 },
  { key: 'approvalTimeoutSeconds', min: 1, max: 900 },
  { key: 'phaseTimeoutSeconds', min: 1, max: 1800 },
  { key: 'totalTimeoutMinutes', min: 1, max: 180 },
]

export default function SettingsPage() {
  const { t, i18n } = useTranslation()
  const [theme, setTheme] = useTheme()
  const [settings, setSettings] = useState<TradeSettings | null>(null)
  const [engines, setEngines] = useState<Engine[]>([])
  const [saved, setSaved] = useState(false)
  const [me, setMe] = useState<MyTrainer>({ name: '', tid: 0, sid: null, gender: 0, badges: 8, hasNationalDex: true })
  const [meSaved, setMeSaved] = useState(false)
  const [meErrors, setMeErrors] = useState<string[]>([])
  const [received, setReceived] = useState<Pokemon[]>([])
  const [pickOpen, setPickOpen] = useState(false)

  useEffect(() => {
    api.get<TradeSettings>('/api/settings/trade').then(setSettings).catch(() => undefined)
    api.get<Engine[]>('/api/engines').then(setEngines).catch(() => undefined)
    api.get<MyTrainer | undefined>('/api/settings/my-trainer').then((m) => m && setMe(m)).catch(() => undefined)
    api.get<Pokemon[]>('/api/pokemon').then((list) => setReceived(list.filter((p) => p.origin === 'received'))).catch(() => undefined)
  }, [])

  async function saveMe(next: MyTrainer = me) {
    const errors = trainerNameErrors(next.name)
    setMeErrors(errors)
    if (errors.length > 0) return
    setMe(await api.put<MyTrainer>('/api/settings/my-trainer', next))
    setMeSaved(true)
    setTimeout(() => setMeSaved(false), 2000)
  }

  async function fromPokemon(id: string) {
    setMe(await api.post<MyTrainer>(`/api/settings/my-trainer/from-pokemon/${id}`))
    setPickOpen(false)
    setMeSaved(true)
    setTimeout(() => setMeSaved(false), 2000)
  }

  async function save() {
    if (!settings) return
    setSettings(await api.put<TradeSettings>('/api/settings/trade', settings))
    setSaved(true)
    setTimeout(() => setSaved(false), 2000)
  }

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">{t('settings.title')}</h1>

      <Card className="flex flex-wrap items-center justify-between gap-2">
        <span className="font-semibold">{t('wizard.title')}</span>
        <Link to="/configurar" className="rounded-lg bg-sky-600 px-4 py-2 text-sm font-medium text-white hover:bg-sky-700">
          {t('wizard.bannerAction')}
        </Link>
      </Card>

      <Card className="space-y-3">
        <h2 className="font-semibold">{t('settings.appearance')}</h2>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label={t('settings.theme')}>
            <select className={inputClass} value={theme} onChange={(e) => setTheme(e.target.value as ThemeChoice)}>
              <option value="system">{t('settings.themeSystem')}</option>
              <option value="light">{t('settings.themeLight')}</option>
              <option value="dark">{t('settings.themeDark')}</option>
            </select>
          </Field>
          <Field label={t('settings.language')}>
            <select className={inputClass} value={i18n.language} onChange={(e) => void i18n.changeLanguage(e.target.value)}>
              {languages.map((l) => (
                <option key={l.code} value={l.code}>
                  {l.label}
                </option>
              ))}
            </select>
          </Field>
        </div>
      </Card>

      <Card className="space-y-3">
        <h2 className="font-semibold">{t('settings.myTrainerTitle')}</h2>
        <ContentFile name="builder/meu-treinador" />
        <div className="grid gap-3 sm:grid-cols-4">
          <Field label={t('trade.trainer.name')}>
            <input className={inputClass} maxLength={7} value={me.name} onChange={(e) => setMe({ ...me, name: e.target.value })} />
          </Field>
          <Field label={t('trade.trainer.tid')}>
            <input type="number" min={0} max={65535} className={inputClass} value={me.tid} onChange={(e) => setMe({ ...me, tid: Number(e.target.value) || 0 })} />
          </Field>
          <Field label={`${t('trade.trainer.sid')} (${t('common.optional')})`}>
            <input
              type="number"
              min={0}
              max={65535}
              className={inputClass}
              value={me.sid ?? ''}
              onChange={(e) => setMe({ ...me, sid: e.target.value === '' ? null : Number(e.target.value) })}
            />
          </Field>
          <Field label={t('settings.trainerGender')}>
            <select className={inputClass} value={me.gender} onChange={(e) => setMe({ ...me, gender: Number(e.target.value) })}>
              <option value={0}>♂</option>
              <option value={1}>♀</option>
            </select>
          </Field>
          <Field label={t('settings.badges')}>
            <input type="number" min={0} max={8} className={inputClass} value={me.badges} onChange={(e) => setMe({ ...me, badges: Math.max(0, Math.min(8, Number(e.target.value) || 0)) })} />
          </Field>
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={me.hasNationalDex} onChange={(e) => setMe({ ...me, hasNationalDex: e.target.checked })} />
          {t('settings.nationalDex')}
        </label>
        {meErrors.length > 0 && (
          <ul className="text-sm text-red-700 dark:text-red-400">
            {meErrors.map((e) => (
              <li key={e}>{t(`errors.${e}`)}</li>
            ))}
          </ul>
        )}
        <div className="flex flex-wrap gap-2">
          <Button onClick={() => void saveMe()}>{meSaved ? t('common.saved') : t('common.save')}</Button>
          <Button variant="secondary" disabled={received.length === 0} onClick={() => setPickOpen(!pickOpen)}>
            {t('settings.fromPokemon')}
          </Button>
        </div>
        {pickOpen && (
          <div className="space-y-1">
            <p className="text-sm text-slate-500">{t('settings.choosePokemon')}</p>
            {received.map((p) => (
              <button
                key={p.id}
                type="button"
                className="block w-full rounded-lg px-3 py-1.5 text-left text-sm hover:bg-slate-100 dark:hover:bg-slate-800"
                onClick={() => void fromPokemon(p.id)}
              >
                {p.nickname || p.speciesName} · {p.otName} · {p.tid}:{p.sid}
              </button>
            ))}
          </div>
        )}
      </Card>

      <Card className="space-y-3">
        <h2 className="font-semibold">{t('settings.engineTitle')}</h2>
        <p className="text-sm text-slate-500">{t('settings.engineHelp')}</p>
        <ul className="space-y-2">
          {engines.map((engine) => (
            <li key={engine.id} className="flex items-center gap-2 text-sm">
              <input
                type="radio"
                name="engine"
                checked={engine.selected}
                disabled={!engine.available}
                onChange={() =>
                  void api.put('/api/engines/selected', { id: engine.id }).then(() =>
                    setEngines((list) => list.map((e) => ({ ...e, selected: e.id === engine.id }))),
                  )
                }
              />
              {t(`settings.engines.${engine.id}`, { defaultValue: engine.id })}
              {!engine.available && engine.reasonCode && (
                <span className="text-xs text-slate-500">({t(`settings.engineReasons.${engine.reasonCode}`, { defaultValue: engine.reasonCode })})</span>
              )}
            </li>
          ))}
        </ul>
      </Card>

      {settings && (
        <Card className="space-y-3">
          <h2 className="font-semibold">{t('settings.tradeTitle')}</h2>
          <div className="grid gap-3 sm:grid-cols-2">
            {numberFields.map((f) => (
              <Field key={f.key} label={t(`settings.${f.key}`)} help={f.help ? t(f.help) : undefined}>
                <input
                  type="number"
                  className={inputClass}
                  min={f.min}
                  max={f.max}
                  value={settings[f.key]}
                  onChange={(e) => setSettings({ ...settings, [f.key]: Number(e.target.value) })}
                />
              </Field>
            ))}
          </div>
          <div className="space-y-1 border-t border-slate-200 pt-3 dark:border-slate-700">
            <h3 className="text-sm font-semibold">{t('settings.illegalTitle')}</h3>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={settings.allowIllegal} onChange={(e) => setSettings({ ...settings, allowIllegal: e.target.checked })} />
              {t('settings.allowIllegal')}
            </label>
            <p className="text-xs text-slate-500">{t('settings.allowIllegalHelp')}</p>
          </div>
          <Button onClick={() => void save()}>{saved ? t('common.saved') : t('common.save')}</Button>
        </Card>
      )}
    </div>
  )
}
