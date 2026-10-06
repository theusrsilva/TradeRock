import { useEffect, useState } from 'react'
import { api } from './api'
import type { BuilderData } from './types'

let cached: Promise<BuilderData> | null = null

/** Listas do construtor (espécies, golpes, itens, naturezas...), pedidas uma vez só. */
export function useBuilderData(): BuilderData | null {
  const [data, setData] = useState<BuilderData | null>(null)
  useEffect(() => {
    cached ??= api.get<BuilderData>('/api/builder/data')
    cached.then(setData).catch(() => {
      cached = null
    })
  }, [])
  return data
}

export const optionText = (list: { value: number; text: string }[] | undefined, value: number | null | undefined) =>
  value == null ? undefined : list?.find((o) => o.value === value)?.text
