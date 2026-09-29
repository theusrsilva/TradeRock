import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { inputClass } from '../../components/ui'
import type { LegalityReport, SpecDifference } from '../../lib/types'

export type Issue = 'invalid' | 'fishy' | 'changed'

/** Campos com problema no resultado (para destacar no formulário). */
export function issuesFrom(report: LegalityReport | null | undefined, differences: SpecDifference[] = []): Record<string, Issue> {
  const issues: Record<string, Issue> = {}
  for (const d of differences) issues[d.field] = 'changed'
  for (const c of report?.checks ?? []) {
    if (!c.field || c.severity === 'valid') continue
    if (c.severity === 'invalid' || issues[c.field] !== 'invalid') issues[c.field] = c.severity === 'invalid' ? 'invalid' : 'fishy'
  }
  return issues
}

const ring: Record<Issue, string> = {
  invalid: 'ring-2 ring-red-500',
  fishy: 'ring-2 ring-amber-400',
  changed: 'ring-2 ring-sky-400',
}

/** Rótulo + campo, com anel colorido quando o resultado tem problema nesse campo. */
export function FieldBox({ field, issue, children, help }: { field: string; issue?: Issue; children: ReactNode; help?: string }) {
  const { t } = useTranslation()
  return (
    <div className="space-y-1 text-sm">
      <span className="block font-medium">{t(`builder.fields.${field}`)}</span>
      <div className={`rounded-lg ${issue ? ring[issue] : ''}`}>{children}</div>
      {help && <span className="block text-xs text-slate-500 dark:text-slate-400">{help}</span>}
    </div>
  )
}

export function StatInputs({ values, max, onChange, disabled = false }: { values: number[]; max: number; onChange: (v: number[]) => void; disabled?: boolean }) {
  const { t } = useTranslation()
  const labels = t('builder.stats', { returnObjects: true }) as string[]
  return (
    <div className="grid grid-cols-3 gap-2 sm:grid-cols-6">
      {values.map((v, i) => (
        <label key={i} className="text-xs">
          <span className="block text-center text-slate-500">{labels[i]}</span>
          <input
            type="number"
            min={0}
            max={max}
            disabled={disabled}
            className={`${inputClass} px-2 text-center`}
            value={v}
            onChange={(e) => {
              const next = [...values]
              next[i] = Math.max(0, Math.min(max, Number(e.target.value) || 0))
              onChange(next)
            }}
          />
        </label>
      ))}
    </div>
  )
}

export const selectClass = inputClass
