import { useTranslation } from 'react-i18next'
import type { LegalityReport, LegalityStatus, TradeWarning } from '../lib/types'
import { Badge } from './ui'

const tones = { legal: 'emerald', illegal: 'red', unverifiable: 'amber' } as const

export function LegalityBadge({ status }: { status: LegalityStatus | null }) {
  const { t } = useTranslation()
  if (!status) return <Badge>{t('legality.unknown')}</Badge>
  const icon = status === 'legal' ? '✓' : status === 'illegal' ? '✕' : '?'
  return (
    <Badge tone={tones[status]}>
      {icon} {t(`legality.${status}`)}
    </Badge>
  )
}

/** Painel com o resultado do LegalityAnalysis: status, encontro e cada verificação explicada. */
export function LegalityPanel({ report, busy = false }: { report: LegalityReport | null; busy?: boolean }) {
  const { t } = useTranslation()
  if (!report) return null
  const problems = report.checks.filter((c) => c.severity !== 'valid')

  return (
    <div className={`space-y-3 ${busy ? 'opacity-60' : ''}`} aria-busy={busy}>
      <div className="flex flex-wrap items-center gap-2">
        <LegalityBadge status={report.status} />
        <span className="text-xs text-slate-500">{t('legality.pkhex', { version: report.pkhexVersion })}</span>
      </div>
      {report.status === 'unverifiable' && (
        <p className="text-sm text-amber-800 dark:text-amber-300">{t('legality.unverifiableExplain')}</p>
      )}
      {(report.encounter || report.encounterLocation) && (
        <p className="text-sm text-slate-600 dark:text-slate-300">
          {report.encounter && t('legality.encounter', { name: report.encounter })}
          {report.encounterLocation && <> · {t('legality.location', { name: report.encounterLocation })}</>}
        </p>
      )}
      <div>
        <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-500">{t('legality.problems')}</h3>
        {problems.length === 0 ? (
          <p className="text-sm text-emerald-700 dark:text-emerald-400">{t('legality.noProblems')}</p>
        ) : (
          <ul className="space-y-1 text-sm">
            {problems.map((c, i) => (
              <li key={i} className="flex gap-2">
                <span className={c.severity === 'invalid' ? 'text-red-600' : 'text-amber-600'} aria-hidden>
                  {c.severity === 'invalid' ? '✕' : '!'}
                </span>
                <span>
                  {c.field && <strong className="mr-1">{t(`builder.fields.${c.field}`, { defaultValue: c.field })}{c.moveIndex !== null ? ` ${c.moveIndex + 1}` : ''}:</strong>}
                  {c.message}
                </span>
              </li>
            ))}
          </ul>
        )}
        {problems.some((c) => c.severity === 'fishy') && <p className="mt-2 text-xs text-slate-500">{t('legality.fishyNote')}</p>}
      </div>
    </div>
  )
}

export function WarningList({ warnings }: { warnings: TradeWarning[] }) {
  const { t } = useTranslation()
  if (warnings.length === 0) return null
  return (
    <ul className="space-y-0.5">
      {warnings.map((w, i) => (
        <li key={i} className="text-xs text-amber-700 dark:text-amber-400">
          ⚠ {t(`warnings.${w.code}`, w.args)}
        </li>
      ))}
    </ul>
  )
}
