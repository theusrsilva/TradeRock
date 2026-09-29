import { useEffect, useState } from 'react'
import { api } from './api'
import type { Health } from './types'

let cached: Promise<Health> | null = null

/** /api/health, pedido uma vez só. `window` diz se estamos na janela nativa (Photino). */
export function useAppInfo(): Health | null {
  const [info, setInfo] = useState<Health | null>(null)
  useEffect(() => {
    cached ??= api.get<Health>('/api/health')
    cached.then(setInfo).catch(() => undefined)
  }, [])
  return info
}

/** Baixa um arquivo (modo navegador). Na janela nativa, use os diálogos do sistema pelo backend. */
export function downloadBlob(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = name
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

export async function postForBlob(path: string, body: unknown): Promise<{ blob: Blob; name: string }> {
  const response = await fetch(path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) throw new Error(`HTTP ${response.status}`)
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1] ?? 'traderock.zip'
  return { blob: await response.blob(), name: decodeURIComponent(name) }
}
