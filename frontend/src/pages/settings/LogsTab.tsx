import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Callout, Toggle, useToast } from '../../components/ui'
import { api } from '../../lib/api'
import { downloadBlob } from '../../lib/appInfo'

interface LogLine {
  at: string
  level: string
  category: string
  message: string
}

const levelLook: Record<string, string> = {
  ERR: 'text-bad',
  WRN: 'text-warn',
  INF: 'text-muted',
}

/** Aba Logs (D28): o que o app, o pokeldn e o esptool registraram, já sem chaves nem dados. */
export function LogsTab() {
  const { t, i18n } = useTranslation()
  const toast = useToast()
  const [lines, setLines] = useState<LogLine[] | null>(null)
  const [keepDays, setKeepDays] = useState(7)
  const [problems, setProblems] = useState(false)
  const [failed, setFailed] = useState(false)

  const load = useCallback(async () => {
    try {
      const res = await api.get<{ lines: LogLine[]; keepDays: number }>(`/api/logs?lines=500&problems=${problems}`)
      setLines(res.lines)
      setKeepDays(res.keepDays)
      setFailed(false)
    } catch {
      setFailed(true)
    }
  }, [problems])

  useEffect(() => {
    void load()
  }, [load])

  const time = (at: string) => new Date(at).toLocaleString(i18n.language, { dateStyle: 'short', timeStyle: 'medium' })
  const asText = (list: LogLine[]) => list.map((l) => `${l.at} ${l.level} ${l.category}: ${l.message}`).join('\n')

  async function copy() {
    if (!lines) return
    try {
      await navigator.clipboard.writeText(asText(lines))
      toast(t('logs.copied'))
    } catch {
      toast(t('logs.copyFailed'))
    }
  }

  async function save() {
    const response = await fetch('/api/logs/file', { credentials: 'same-origin' })
    downloadBlob(await response.blob(), `traderock-log-${new Date().toISOString().slice(0, 10)}.txt`)
  }

  return (
    <div className="flex flex-col gap-3.5 rounded-xl border border-line bg-surface px-[22px] py-5">
      <div className="flex flex-wrap items-center gap-3">
        <div className="min-w-[200px] flex-1">
          <div className="text-base font-extrabold">{t('logs.title')}</div>
          <div className="text-[13.5px] text-muted">{t('logs.sub', { days: keepDays })}</div>
        </div>
        <Toggle small on={problems} onChange={setProblems} label={t('logs.problemsOnly')} />
        <Button variant="secondary" icon="refresh" onClick={() => void load()}>
          {t('logs.refresh')}
        </Button>
        <Button variant="secondary" icon="content_copy" disabled={!lines?.length} onClick={() => void copy()}>
          {t('logs.copy')}
        </Button>
        <Button variant="secondary" icon="download" onClick={() => void save()}>
          {t('logs.save')}
        </Button>
      </div>
      <Callout tone="sky" icon="lock">
        {t('logs.privacy')}
      </Callout>
      {failed && <p className="text-sm text-bad">{t('logs.loadFailed')}</p>}
      {lines && lines.length === 0 && <p className="text-sm text-muted">{t(problems ? 'logs.noProblems' : 'logs.empty')}</p>}
      {lines && lines.length > 0 && (
        <ol className="max-h-[60vh] overflow-auto rounded-lg bg-surface2 p-3 font-mono text-xs leading-relaxed">
          {[...lines].reverse().map((l, i) => (
            <li key={`${l.at}-${i}`} className="border-b border-line py-1.5 last:border-0">
              <span className="text-muted">{time(l.at)}</span> <span className={`font-bold ${levelLook[l.level] ?? ''}`}>{t(`logs.levels.${l.level}`, { defaultValue: l.level })}</span>{' '}
              <span className="text-muted">{l.category}</span>
              <pre className="mt-0.5 break-words whitespace-pre-wrap">{l.message}</pre>
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}
