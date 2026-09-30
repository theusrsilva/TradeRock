import { useRef, useState, type DragEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../lib/api'
import type { UploadResult } from '../lib/types'
import { Button } from './ui'

/** Importar .pk3/.ek3 por botão ou arrastando para a área. */
export function UploadArea({ onImported }: { onImported: () => void }) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const [messages, setMessages] = useState<{ text: string; ok: boolean }[]>([])
  const [dragging, setDragging] = useState(false)
  const [busy, setBusy] = useState(false)

  async function upload(files: FileList | File[]) {
    const list = Array.from(files)
    if (list.length === 0) return
    const form = new FormData()
    for (const file of list) form.append('files', file, file.name)
    setBusy(true)
    try {
      const results = await api.post<UploadResult[]>('/api/pokemon', form)
      setMessages(
        results.map((r) => ({
          ok: r.ok,
          text: r.ok
            ? t(r.created ? 'pokemon.uploadCreated' : 'pokemon.uploadDuplicate', { file: r.fileName })
            : t('pokemon.uploadFailed', {
                file: r.fileName,
                reason: t(`pokemon.fileErrors.${r.error}`, { defaultValue: r.errorMessage ?? '' }),
              }),
        })),
      )
      onImported()
    } catch {
      setMessages([{ ok: false, text: t('common.error') }])
    } finally {
      setBusy(false)
    }
  }

  function onDrop(e: DragEvent) {
    e.preventDefault()
    setDragging(false)
    void upload(e.dataTransfer.files)
  }

  return (
    <div
      onDragOver={(e) => {
        e.preventDefault()
        setDragging(true)
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={onDrop}
      className={`rounded-xl border-2 border-dashed p-4 text-center text-sm transition ${
        dragging ? 'border-sky-500 bg-sky-50 dark:bg-sky-950/30' : 'border-slate-300 dark:border-slate-700'
      }`}
    >
      <input
        ref={input}
        type="file"
        accept=".pk3,.ek3"
        multiple
        hidden
        onChange={(e) => {
          if (e.target.files) void upload(e.target.files)
          e.target.value = ''
        }}
      />
      <Button variant="secondary" disabled={busy} onClick={() => input.current?.click()}>
        {t('pokemon.import')}
      </Button>
      <p className="mt-2 text-slate-500 dark:text-slate-400">{t('pokemon.dropHint')}</p>
      {messages.length > 0 && (
        <ul className="mt-3 space-y-1 text-left">
          {messages.map((m, i) => (
            <li key={i} className={m.ok ? 'text-emerald-700 dark:text-emerald-400' : 'text-red-700 dark:text-red-400'}>
              {m.text}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
