import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { HelpTip, inputClass } from '../../components/ui'
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
  invalid: 'ring-2 ring-bad',
  fishy: 'ring-2 ring-warn',
  changed: 'ring-2 ring-info',
}

/** Rótulo + campo, com anel colorido quando o resultado tem problema nesse campo. */
export function FieldBox({
  field,
  issue,
  children,
  help,
  label,
  tip,
}: {
  field: string
  issue?: Issue
  children: ReactNode
  help?: string
  label?: ReactNode
  tip?: ReactNode
}) {
  const { t } = useTranslation()
  return (
    <div className="flex min-w-0 flex-col gap-1.5 text-sm">
      <span className="flex items-center gap-1.5 font-bold">
        {label ?? t(`builder.fields.${field}`)}
        {tip && <HelpTip>{tip}</HelpTip>}
      </span>
      <div className={`rounded-lg ${issue ? ring[issue] : ''}`}>{children}</div>
      {help && <span className="block text-[12.5px] text-muted">{help}</span>}
    </div>
  )
}

export function StatInputs({ values, max, onChange, disabled = false }: { values: number[]; max: number; onChange: (v: number[]) => void; disabled?: boolean }) {
  const { t } = useTranslation()
  const labels = t('builder.stats', { returnObjects: true }) as string[]
  return (
    <div className="grid gap-1.5 [grid-template-columns:repeat(auto-fit,minmax(56px,1fr))]">
      {values.map((v, i) => (
        <label key={i} className="flex flex-col items-center gap-[3px] text-xs">
          <span className="block text-center text-muted">{labels[i]}</span>
          <input
            type="number"
            min={0}
            max={max}
            disabled={disabled}
            className={`${inputClass} h-[38px]! px-1! text-center!`}
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
