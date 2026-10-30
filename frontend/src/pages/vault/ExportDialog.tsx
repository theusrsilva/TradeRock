import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Modal } from '../../components/ui'
import { api } from '../../lib/api'
import { downloadBlob, postForBlob, useAppInfo } from '../../lib/appInfo'

export function ExportDialog({ ids, onClose }: { ids: string[]; onClose: () => void }) {
  const { t } = useTranslation()
  const info = useAppInfo()
  const [format, setFormat] = useState<'pk3' | 'ek3'>('pk3')
  const [size, setSize] = useState<100 | 80>(100)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  async function run() {
    setBusy(true)
    setMessage(null)
    try {
      if (info?.window) {
        // Janela nativa: o sistema pergunta onde salvar.
        const r = await api.post<{ written: string[] } | undefined>('/api/vault/export/native', { ids, format, size })
        if (r) setMessage(t('vault.exported', { count: r.written.length }))
      } else if (ids.length === 1) {
        const response = await fetch(`/api/pokemon/${ids[0]}/file?format=${format}&size=${size}`, { credentials: 'same-origin' })
        const name = /filename="?([^";]+)"?/.exec(response.headers.get('Content-Disposition') ?? '')?.[1] ?? `pokemon.${format}`
        downloadBlob(await response.blob(), name)
        setMessage(t('vault.exported', { count: 1 }))
      } else {
        const { blob, name } = await postForBlob('/api/vault/export', { ids, format, size })
        downloadBlob(blob, name)
        setMessage(t('vault.exported', { count: ids.length }))
      }
    } catch {
      setMessage(t('common.error'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t('vault.exportTitle')}
      actions={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={busy} onClick={() => void run()}>
            {t('vault.export')} ({ids.length})
          </Button>
        </>
      }
    >
      <fieldset className="space-y-1">
        <legend className="font-medium">{t('vault.format')}</legend>
        {(['pk3', 'ek3'] as const).map((f) => (
          <label key={f} className="flex items-center gap-2">
            <input type="radio" checked={format === f} onChange={() => setFormat(f)} /> {t(f === 'pk3' ? 'vault.formatPk3' : 'vault.formatEk3')}
          </label>
        ))}
      </fieldset>
      <fieldset className="mt-3 space-y-1">
        <legend className="font-medium">{t('vault.size')}</legend>
        {([100, 80] as const).map((s) => (
          <label key={s} className="flex items-center gap-2">
            <input type="radio" checked={size === s} onChange={() => setSize(s)} /> {t(s === 100 ? 'vault.size100' : 'vault.size80')}
          </label>
        ))}
      </fieldset>
      <p className="mt-3 text-[13px] text-muted">{t('vault.exportSwitchNote')}</p>
      {message && <p className="mt-3 text-ok">{message}</p>}
    </Modal>
  )
}
