import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { LegalityBadge } from '../../components/Legality'
import { Button, Modal } from '../../components/ui'
import { ApiError, api, spriteUrl } from '../../lib/api'
import type { SaveUpload } from '../../lib/types'

/** Trazer Pokémon de um save de GBA para o cofre. */
export function SaveImportDialog({ initialFile, onClose, onImported }: { initialFile: File | null; onClose: () => void; onImported: () => void }) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const [upload, setUpload] = useState<SaveUpload | null>(null)
  const [selected, setSelected] = useState<number[]>([])
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null)
  const [busy, setBusy] = useState(false)

  async function send(file: File) {
    setBusy(true)
    setMessage(null)
    const form = new FormData()
    form.append('file', file, file.name)
    try {
      setUpload(await api.post<SaveUpload>('/api/pokemon/sav', form))
      setSelected([])
    } catch (e) {
      const code = e instanceof ApiError ? e.errors[0] : undefined
      setMessage({ ok: false, text: code ? t(`pokemon.savErrors.${code}`, { defaultValue: t('common.error') }) : t('common.error') })
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    if (initialFile) void send(initialFile)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialFile])

  async function importSelected() {
    if (!upload) return
    setBusy(true)
    try {
      const results = await api.post<unknown[]>(`/api/pokemon/sav/${upload.token}/import`, { indices: selected })
      setMessage({ ok: true, text: t('pokemon.savDone', { count: results.length }) })
      setUpload(null)
      onImported()
    } catch (e) {
      const code = e instanceof ApiError ? e.code : undefined
      setMessage({ ok: false, text: code ? t(`pokemon.savErrors.${code}`, { defaultValue: t('common.error') }) : t('common.error') })
    } finally {
      setBusy(false)
    }
  }

  const toggle = (index: number) => setSelected((s) => (s.includes(index) ? s.filter((i) => i !== index) : [...s, index]))

  return (
    <Modal
      title={t('pokemon.importSav')}
      actions={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          {upload && (
            <Button disabled={selected.length === 0 || busy} onClick={() => void importSelected()}>
              {t('pokemon.savImport', { count: selected.length })}
            </Button>
          )}
        </>
      }
    >
      <div className="space-y-3">
        <p>{t('pokemon.savIntro')}</p>
        <input ref={input} type="file" accept=".sav,.srm,.dsv,.fla" hidden onChange={(e) => { if (e.target.files?.[0]) void send(e.target.files[0]); e.target.value = '' }} />
        {!upload && (
          <Button variant="secondary" disabled={busy} onClick={() => input.current?.click()}>
            {t('pokemon.savChoose')}
          </Button>
        )}
        {message && <p className={message.ok ? 'text-ok' : 'text-bad'}>{message.text}</p>}
        {upload && (
          <>
            <p className="font-medium">{t('pokemon.savHeader', { game: upload.game, trainer: upload.trainerName, tid: upload.tid })}</p>
            <Button variant="ghost" onClick={() => setSelected(upload.entries.map((e) => e.index))}>
              {t('pokemon.savSelectAll')}
            </Button>
            <ul className="max-h-72 space-y-1 overflow-y-auto">
              {upload.entries.map((e) => (
                <li key={e.index}>
                  <label className="flex items-center gap-2 rounded-lg px-2 py-1 hover:bg-surface2">
                    <input type="checkbox" checked={selected.includes(e.index)} onChange={() => toggle(e.index)} />
                    <img src={spriteUrl(e.summary.species)} alt="" className="h-8 w-8 [image-rendering:pixelated]" />
                    <span className="flex-1">
                      {e.summary.nickname} · {t('pokemon.level', { level: e.summary.level })}
                      <span className="block text-xs text-muted">
                        {e.place === 'party' ? t('pokemon.savParty', { n: e.slot + 1 }) : t('pokemon.savBox', { box: e.box + 1, slot: e.slot + 1 })}
                      </span>
                    </span>
                    <LegalityBadge status={e.legality} />
                  </label>
                </li>
              ))}
            </ul>
          </>
        )}
      </div>
    </Modal>
  )
}
