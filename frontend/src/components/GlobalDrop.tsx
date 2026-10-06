import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { api } from '../lib/api'
import { notifyVaultChanged, setPendingSave } from '../lib/pendingDrop'
import type { UploadResult } from '../lib/types'

export const PENDING_SAVE = 'traderock:pending-save'

const isSave = (name: string) => /\.(sav|srm|fla|dsv)$/i.test(name)
const isPokemon = (name: string) => /\.(pk3|ek3)$/i.test(name)

/**
 * Soltar arquivos em qualquer lugar da janela: .pk3/.ek3 vão para o cofre; um .sav abre o
 * cofre com a escolha de Pokémon. Arrastes internos (Pokémon entre caixas) não têm "Files".
 */
export function GlobalDrop() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [active, setActive] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const depth = useRef(0)

  useEffect(() => {
    const hasFiles = (e: DragEvent) => e.dataTransfer?.types.includes('Files') ?? false

    const enter = (e: DragEvent) => {
      if (!hasFiles(e)) return
      depth.current++
      setActive(true)
    }
    const leave = (e: DragEvent) => {
      if (!hasFiles(e)) return
      depth.current = Math.max(0, depth.current - 1)
      if (depth.current === 0) setActive(false)
    }
    const over = (e: DragEvent) => {
      if (hasFiles(e)) e.preventDefault()
    }
    const drop = async (e: DragEvent) => {
      depth.current = 0
      setActive(false)
      if (!hasFiles(e) || e.defaultPrevented) return // uma área de envio já tratou
      e.preventDefault()
      const files = Array.from(e.dataTransfer?.files ?? [])

      const save = files.find((f) => isSave(f.name))
      if (save) {
        setPendingSave(save)
        navigate('/pokemon')
        window.dispatchEvent(new Event(PENDING_SAVE))
        return
      }

      const mons = files.filter((f) => isPokemon(f.name))
      if (mons.length === 0) return
      const form = new FormData()
      for (const f of mons) form.append('files', f, f.name)
      try {
        const results = await api.post<UploadResult[]>('/api/pokemon', form)
        setMessage(
          t('vault.dropDone', {
            created: results.filter((r) => r.ok && r.created).length,
            duplicates: results.filter((r) => r.ok && !r.created).length,
            failed: results.filter((r) => !r.ok).length,
          }),
        )
        notifyVaultChanged()
      } catch {
        setMessage(t('common.error'))
      }
      setTimeout(() => setMessage(null), 5000)
    }

    window.addEventListener('dragenter', enter)
    window.addEventListener('dragleave', leave)
    window.addEventListener('dragover', over)
    window.addEventListener('drop', drop)
    return () => {
      window.removeEventListener('dragenter', enter)
      window.removeEventListener('dragleave', leave)
      window.removeEventListener('dragover', over)
      window.removeEventListener('drop', drop)
    }
  }, [navigate, t])

  return (
    <>
      {active && (
        <div className="pointer-events-none fixed inset-0 z-40 flex items-center justify-center bg-accent/20 backdrop-blur-sm">
          <div className="rounded-2xl border-2 border-dashed border-accent bg-surface px-8 py-6 text-center shadow-xl">
            <p className="font-display text-xl font-bold">{t('vault.dropToImport')}</p>
            <p className="text-sm text-muted">{t('vault.dropHint')}</p>
          </div>
        </div>
      )}
      {message && (
        <div className="fixed right-4 bottom-[84px] z-40 rounded-lg bg-ink px-4 py-2.5 text-sm font-semibold text-bg shadow-lg md:bottom-4" role="status">
          {message}
        </div>
      )}
    </>
  )
}
