import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Badge, Button } from '../../components/ui'
import type { CheckStatus, CompatStatus, Esp32Entry, HardwareCatalog, SetupCheck, WifiEntry } from '../../lib/types'

const checkTone: Record<CheckStatus, 'emerald' | 'amber' | 'red' | 'sky' | 'slate'> = {
  ok: 'emerald',
  warning: 'amber',
  error: 'red',
  pending: 'sky',
  info: 'slate',
}
const checkIcon: Record<CheckStatus, string> = { ok: '✓', warning: '!', error: '✕', pending: '…', info: 'i' }

export const compatTone: Record<CompatStatus, 'emerald' | 'amber' | 'red' | 'sky' | 'slate'> = {
  recommended: 'emerald',
  works: 'emerald',
  untested: 'slate',
  unstable: 'amber',
  problematic: 'red',
  unsupported: 'red',
}

export function CompatBadge({ status }: { status: CompatStatus }) {
  const { t } = useTranslation()
  return <Badge tone={compatTone[status]}>{t(`wizard.compat.${status}`)}</Badge>
}

function CopyCommand({ command }: { command: string }) {
  const { t } = useTranslation()
  const [copied, setCopied] = useState(false)
  return (
    <div className="mt-1 flex items-start gap-2">
      <code className="block flex-1 overflow-x-auto whitespace-pre rounded bg-slate-900 px-2 py-1 text-xs text-slate-100">{command}</code>
      <Button
        variant="secondary"
        className="px-2 py-1 text-xs"
        onClick={() =>
          void navigator.clipboard?.writeText(command).then(() => {
            setCopied(true)
            setTimeout(() => setCopied(false), 1500)
          })
        }
      >
        {copied ? t('wizard.copied') : t('wizard.copy')}
      </Button>
    </div>
  )
}

/** Verificações de uma etapa: status, explicação e "como resolver". */
export function CheckList({ checks }: { checks: SetupCheck[] }) {
  const { t } = useTranslation()
  if (checks.length === 0) return null
  return (
    <ul className="space-y-2">
      {checks.map((c) => (
        <li key={c.id} className="rounded-lg border border-slate-200 p-3 text-sm dark:border-slate-700">
          <div className="flex items-start gap-2">
            <Badge tone={checkTone[c.status]}>
              {checkIcon[c.status]} {t(`wizard.checkStatus.${c.status}`)}
            </Badge>
            <div className="flex-1">
              <p>
                {t(`wizard.checks.${c.code}`, c.args ?? {})}
                {c.essential && c.status !== 'ok' && <span className="ml-1 text-xs text-slate-500">({t('wizard.essential')})</span>}
              </p>
              {c.availableIn && (
                <p className="text-xs text-sky-700 dark:text-sky-400">
                  {t('wizard.availableIn', { what: t(`wizard.deliveries.${c.availableIn}`, { defaultValue: c.availableIn }) })}
                </p>
              )}
              {c.args?.url && (
                <a href={c.args.url} target="_blank" rel="noreferrer" className="text-xs text-sky-700 underline dark:text-sky-400">
                  {t('wizard.driverLink')}
                </a>
              )}
              {c.fix && (
                <details className="mt-1">
                  <summary className="cursor-pointer text-xs font-medium text-sky-700 dark:text-sky-400">{t('wizard.howToFix')}</summary>
                  <CopyCommand command={c.fix} />
                </details>
              )}
            </div>
          </div>
        </li>
      ))}
    </ul>
  )
}

export function CatalogNotice({ catalog }: { catalog: HardwareCatalog }) {
  const { t, i18n } = useTranslation()
  return (
    <p className="text-xs text-slate-500">
      {t('wizard.catalogDate', { date: new Date(catalog.updatedAt + 'T12:00:00').toLocaleDateString(i18n.language) })}{' '}
      {i18n.language === 'en' ? catalog.notice.en : catalog.notice['pt-BR']}
    </p>
  )
}

export function WifiTable({ entries, highlight }: { entries: WifiEntry[]; highlight: string[] }) {
  const { t, i18n } = useTranslation()
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="text-xs uppercase text-slate-500">
          <tr>
            <th className="py-1 pr-3">{t('wizard.model')}</th>
            <th className="py-1 pr-3">{t('wizard.driver')}</th>
            <th className="py-1 pr-3">{t('wizard.status')}</th>
            <th className="py-1">{t('wizard.notes')}</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((w) => {
            const mine = w.drivers.some((d) => highlight.includes(d))
            return (
              <tr key={w.id} className={`border-t border-slate-200 align-top dark:border-slate-700 ${mine ? 'bg-sky-50 dark:bg-sky-950/40' : ''}`}>
                <td className="py-2 pr-3 font-medium">
                  {w.model} <span className="text-xs text-slate-500">({w.type === 'usb' ? 'USB' : 'interna'})</span>
                  {mine && <Badge tone="sky">{t('wizard.detected')}</Badge>}
                </td>
                <td className="py-2 pr-3 font-mono text-xs">{w.drivers[0]}</td>
                <td className="py-2 pr-3">
                  <CompatBadge status={w.status} />
                </td>
                <td className="py-2 text-xs text-slate-600 dark:text-slate-300">
                  {i18n.language === 'en' ? w.notes.en : w.notes['pt-BR']}{' '}
                  <a href={w.source} target="_blank" rel="noreferrer" className="text-sky-700 underline dark:text-sky-400">
                    {t('wizard.source')}
                  </a>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

export function Esp32Table({ entries, highlight }: { entries: Esp32Entry[]; highlight: string | null }) {
  const { t, i18n } = useTranslation()
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="text-xs uppercase text-slate-500">
          <tr>
            <th className="py-1 pr-3">{t('wizard.model')}</th>
            <th className="py-1 pr-3">{t('wizard.connection')}</th>
            <th className="py-1 pr-3">{t('wizard.status')}</th>
            <th className="py-1">{t('wizard.notes')}</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((e) => (
            <tr key={e.id} className={`border-t border-slate-200 align-top dark:border-slate-700 ${highlight === e.id ? 'bg-sky-50 dark:bg-sky-950/40' : ''}`}>
              <td className="py-2 pr-3 font-medium">{e.model}</td>
              <td className="py-2 pr-3 text-xs">{t(`wizard.connections.${e.connection}`)}</td>
              <td className="py-2 pr-3">
                <CompatBadge status={e.status} />
              </td>
              <td className="py-2 text-xs text-slate-600 dark:text-slate-300">
                {i18n.language === 'en' ? e.notes.en : e.notes['pt-BR']}{' '}
                <a href={e.source} target="_blank" rel="noreferrer" className="text-sky-700 underline dark:text-sky-400">
                  {t('wizard.source')}
                </a>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/** Desenho (sem fotos de terceiros): placa ESP32, módulo com a blindagem e as portas USB. */
export function Esp32Illustration() {
  return (
    <svg viewBox="0 0 340 150" role="img" aria-label="ESP32-WROOM-32E" className="w-full max-w-md text-slate-700 dark:text-slate-200">
      <rect x="10" y="20" width="250" height="110" rx="8" className="fill-slate-800 stroke-slate-600" strokeWidth="2" />
      {Array.from({ length: 15 }, (_, i) => (
        <circle key={`a${i}`} cx={24 + i * 15} cy="30" r="3" className="fill-amber-400" />
      ))}
      {Array.from({ length: 15 }, (_, i) => (
        <circle key={`b${i}`} cx={24 + i * 15} cy="120" r="3" className="fill-amber-400" />
      ))}
      <rect x="90" y="42" width="110" height="66" rx="4" className="fill-slate-300 stroke-slate-400" strokeWidth="2" />
      <rect x="200" y="46" width="40" height="58" className="fill-slate-700" />
      <path d="M206 52 h28 v8 h-22 v8 h22 v8 h-22 v8 h22" className="fill-none stroke-amber-400" strokeWidth="2" />
      <text x="145" y="70" textAnchor="middle" fontSize="11" fontWeight="700" className="fill-slate-800">ESP32-WROOM</text>
      <text x="145" y="88" textAnchor="middle" fontSize="16" fontWeight="800" className="fill-red-600">-32E</text>
      <rect x="0" y="52" width="22" height="18" rx="3" className="fill-slate-400" />
      <rect x="0" y="82" width="22" height="18" rx="3" className="fill-slate-400" />
      <text x="28" y="65" fontSize="9" className="fill-emerald-300">UART / COM</text>
      <text x="28" y="95" fontSize="9" className="fill-slate-300">USB / OTG</text>
      <path d="M300 80 L252 80" stroke="currentColor" strokeWidth="1.5" markerEnd="url(#esp32-arrow)" />
      <defs>
        <marker id="esp32-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto">
          <path d="M0,0 L10,5 L0,10 z" fill="currentColor" />
        </marker>
      </defs>
      <text x="304" y="84" fontSize="14" fontWeight="700" fill="currentColor">E?</text>
    </svg>
  )
}
