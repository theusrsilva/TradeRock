import { useRef, useState, type DragEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../lib/api'
import type { UploadResult } from '../lib/types'
import { Button } from './ui'

/** Importar .pk3/.ek3 por botão ou arrastando para a área. */
export function UploadArea({ onImported, compact = false }: { onImported: () => void; compact?: boolean }) {
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

  const fileInput = (
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
  )
  const messageList = messages.length > 0 && (
    <ul className="mt-2 space-y-1 text-left text-[13px]">
      {messages.map((m, i) => (
        <li key={i} className={m.ok ? 'text-ok' : 'text-bad'}>
          {m.text}
        </li>
      ))}
    </ul>
  )

  // Só o botão (o arrastar e soltar vale para a janela toda).
  if (compact)
    return (
      <div>
        {fileInput}
        <Button variant="secondary" size="sm" icon="upload_file" disabled={busy} onClick={() => input.current?.click()}>
          {t('pokemon.import')}
        </Button>
        {messageList}
      </div>
    )

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
        dragging ? 'border-accent bg-accent-soft' : 'border-line'
      }`}
    >
      {fileInput}
      <Button variant="secondary" icon="upload_file" disabled={busy} onClick={() => input.current?.click()}>
        {t('pokemon.import')}
      </Button>
      <p className="mt-2 text-muted">{t('pokemon.dropHint')}</p>
      {messageList}
    </div>
  )
}
