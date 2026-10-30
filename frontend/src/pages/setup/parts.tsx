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
        <li key={c.id} className="rounded-lg border border-line p-3 text-sm">
          <div className="flex items-start gap-2">
            <Badge tone={checkTone[c.status]}>
              {checkIcon[c.status]} {t(`wizard.checkStatus.${c.status}`)}
            </Badge>
            <div className="flex-1">
              <p>
                {t(`wizard.checks.${c.code}`, c.args ?? {})}
                {c.essential && c.status !== 'ok' && <span className="ml-1 text-xs text-muted">({t('wizard.essential')})</span>}
              </p>
              {c.availableIn && (
                <p className="text-xs text-accent">
                  {t('wizard.availableIn', { what: t(`wizard.deliveries.${c.availableIn}`, { defaultValue: c.availableIn }) })}
                </p>
              )}
              {c.args?.url && (
                <a href={c.args.url} target="_blank" rel="noreferrer" className="text-xs text-accent underline">
                  {t('wizard.driverLink')}
                </a>
              )}
              {c.fix && (
                <details className="mt-1">
                  <summary className="cursor-pointer text-xs font-medium text-accent">{t('wizard.howToFix')}</summary>
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
    <p className="text-xs text-muted">
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
        <thead className="text-xs uppercase text-muted">
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
              <tr key={w.id} className={`border-t border-line align-top ${mine ? 'bg-accent-soft' : ''}`}>
                <td className="py-2 pr-3 font-medium">
                  {w.model} <span className="text-xs text-muted">({w.type === 'usb' ? 'USB' : 'interna'})</span>
                  {mine && <Badge tone="sky">{t('wizard.detected')}</Badge>}
                </td>
                <td className="py-2 pr-3 font-mono text-xs">{w.drivers[0]}</td>
                <td className="py-2 pr-3">
                  <CompatBadge status={w.status} />
                </td>
                <td className="py-2 text-xs text-muted">
                  {i18n.language === 'en' ? w.notes.en : w.notes['pt-BR']}{' '}
                  <a href={w.source} target="_blank" rel="noreferrer" className="text-accent underline">
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
        <thead className="text-xs uppercase text-muted">
          <tr>
            <th className="py-1 pr-3">{t('wizard.model')}</th>
            <th className="py-1 pr-3">{t('wizard.connection')}</th>
            <th className="py-1 pr-3">{t('wizard.status')}</th>
            <th className="py-1">{t('wizard.notes')}</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((e) => (
            <tr key={e.id} className={`border-t border-line align-top ${highlight === e.id ? 'bg-accent-soft' : ''}`}>
              <td className="py-2 pr-3 font-medium">{e.model}</td>
              <td className="py-2 pr-3 text-xs">{t(`wizard.connections.${e.connection}`)}</td>
              <td className="py-2 pr-3">
                <CompatBadge status={e.status} />
              </td>
              <td className="py-2 text-xs text-muted">
                {i18n.language === 'en' ? e.notes.en : e.notes['pt-BR']}{' '}
                <a href={e.source} target="_blank" rel="noreferrer" className="text-accent underline">
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
