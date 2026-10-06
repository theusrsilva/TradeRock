import type { TagColor } from '../../lib/types'

export const tagColors: TagColor[] = ['slate', 'red', 'amber', 'emerald', 'sky', 'violet', 'pink']

export const tagClass: Record<TagColor, string> = {
  slate: 'bg-surface2 text-ink',
  red: 'bg-red-100 text-red-800 dark:bg-red-900/60 dark:text-red-200',
  amber: 'bg-amber-100 text-amber-800 dark:bg-amber-900/60 dark:text-amber-200',
  emerald: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/60 dark:text-emerald-200',
  sky: 'bg-info-soft text-info',
  violet: 'bg-violet-100 text-violet-800 dark:bg-violet-900/60 dark:text-violet-200',
  pink: 'bg-pink-100 text-pink-800 dark:bg-pink-900/60 dark:text-pink-200',
}
