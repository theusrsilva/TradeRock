import { useTranslation } from 'react-i18next'
import { Badge, Button, Icon } from '../../components/ui'
import type { QueueItem, TradeQueue } from '../../lib/types'

export function queueName(item: QueueItem, t: (key: string) => string) {
  return item.name ?? (item.isHost ? t('queue.host') : '?')
}

/** Lista da fila: quem troca agora e quem espera, com a posição de cada um. */
export function QueueList({ queue }: { queue: TradeQueue }) {
  const { t } = useTranslation()
  if (queue.entries.length === 0) return <p className="text-sm text-muted">{t('queue.empty')}</p>
  return (
    <ol className="flex flex-col gap-1 text-sm text-ink">
      {queue.entries.map((item) => (
        <li key={item.id} className={`flex items-center gap-2 rounded-lg px-3 py-1.5 ${item.mine ? 'bg-accent-soft font-bold' : ''}`}>
          <span className="w-16 shrink-0 text-muted">{item.position === 0 ? t('queue.now') : `${item.position}º`}</span>
          <span>{queueName(item, t)}</span>
          {item.mine && <Badge tone="accent">{t('queue.you')}</Badge>}
        </li>
      ))}
    </ol>
  )
}

/** Passo do modo servidor: esperando a vez. A troca começa sozinha quando o rádio fica livre. */
export function QueueStep({ queue, entryId, onLeave, leaving }: { queue: TradeQueue | null; entryId: string; onLeave: () => void; leaving: boolean }) {
  const { t } = useTranslation()
  const mine = queue?.entries.find((e) => e.id === entryId)
  return (
    <div className="mx-auto flex w-full max-w-[880px] flex-col items-center gap-4 rounded-xl border border-line bg-surface px-7 py-9 text-center">
      <span className="flex h-16 w-16 items-center justify-center rounded-full bg-info-soft text-info">
        <Icon name="groups" className="text-[34px]" />
      </span>
      <div className="text-xs font-bold tracking-[1px] text-accent uppercase">{t('queue.title')}</div>
      {mine && mine.position > 0 ? (
        <p className="font-display text-[26px] font-bold" aria-live="polite">
          {t('queue.position', { position: mine.position })}
        </p>
      ) : (
        <p className="font-display text-[22px] font-bold" aria-live="polite">
          {t('queue.starting')}
        </p>
      )}
      <p className="max-w-[520px] text-muted">{t('queue.help')}</p>
      {queue && (
        <div className="w-full max-w-[420px] text-left">
          <QueueList queue={queue} />
        </div>
      )}
      <Button variant="outlineDanger" disabled={leaving || !mine || mine.position === 0} onClick={onLeave}>
        {t('queue.leave')}
      </Button>
    </div>
  )
}
