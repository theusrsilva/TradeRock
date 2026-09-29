import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Card, Field, inputClass } from '../components/ui'
import { api } from '../lib/api'
import { languages } from '../i18n'
import type { Engine, TradeSettings } from '../lib/types'
import { useTheme, type ThemeChoice } from '../theme'

const numberFields: { key: keyof TradeSettings; min: number; max: number; step?: number; help?: string }[] = [
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

  useEffect(() => {
    api.get<TradeSettings>('/api/settings/trade').then(setSettings).catch(() => undefined)
    api.get<Engine[]>('/api/engines').then(setEngines).catch(() => undefined)
  }, [])

  async function save() {
    if (!settings) return
    setSettings(await api.put<TradeSettings>('/api/settings/trade', settings))
    setSaved(true)
    setTimeout(() => setSaved(false), 2000)
  }

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">{t('settings.title')}</h1>

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
          <Button onClick={() => void save()}>{saved ? t('common.saved') : t('common.save')}</Button>
        </Card>
      )}
    </div>
  )
}
