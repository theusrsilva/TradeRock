import { useTranslation } from 'react-i18next'
import { Badge, Button, Card } from '../../components/ui'
import type { QueueItem, TradeQueue } from '../../lib/types'

export function queueName(item: QueueItem, t: (key: string) => string) {
  return item.name ?? (item.isHost ? t('queue.host') : '?')
}

/** Lista da fila: quem troca agora e quem espera, com a posição de cada um. */
export function QueueList({ queue }: { queue: TradeQueue }) {
  const { t } = useTranslation()
  if (queue.entries.length === 0) return <p className="text-sm text-slate-500">{t('queue.empty')}</p>
  return (
    <ol className="space-y-1 text-sm">
      {queue.entries.map((item) => (
        <li
          key={item.id}
          className={`flex items-center gap-2 rounded-lg px-3 py-1.5 ${item.mine ? 'bg-sky-50 font-medium dark:bg-sky-950/40' : ''}`}
        >
          <span className="w-16 shrink-0 text-slate-500">{item.position === 0 ? t('queue.now') : `${item.position}º`}</span>
          <span>{queueName(item, t)}</span>
          {item.mine && <Badge tone="sky">{t('queue.you')}</Badge>}
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
    <Card className="space-y-4">
      <h2 className="font-semibold">{t('queue.title')}</h2>
      {mine && mine.position > 0 ? (
        <p className="text-2xl font-semibold" aria-live="polite">
          {t('queue.position', { position: mine.position })}
        </p>
      ) : (
        <p aria-live="polite">{t('queue.starting')}</p>
      )}
      <p className="text-sm text-slate-600 dark:text-slate-300">{t('queue.help')}</p>
      {queue && <QueueList queue={queue} />}
      <Button variant="secondary" disabled={leaving || !mine || mine.position === 0} onClick={onLeave}>
        {t('queue.leave')}
      </Button>
    </Card>
  )
}
