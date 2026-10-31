import { useEffect, useState } from 'react'
import { api } from './api'
import type { BuilderData, TradeGame } from './types'

const cached = new Map<TradeGame, Promise<BuilderData>>()

/**
 * Listas do construtor (espécies, golpes, itens, naturezas...) de um jogo, pedidas uma vez só por jogo. Sem jogo,
 * as do FireRed (as mesmas do LeafGreen).
 */
export function useBuilderData(game: TradeGame = 'fire_red'): BuilderData | null {
  const [data, setData] = useState<BuilderData | null>(null)
  useEffect(() => {
    let alive = true
    let request = cached.get(game)
    if (!request) {
      request = api.get<BuilderData>(`/api/builder/data?game=${game}`)
      cached.set(game, request)
    }
    request
      .then((d) => alive && setData(d))
      .catch(() => {
        cached.delete(game)
      })
    return () => {
      alive = false
    }
  }, [game])
  return data
}

export const optionText = (list: { value: number; text: string }[] | undefined, value: number | null | undefined) =>
  value == null ? undefined : list?.find((o) => o.value === value)?.text
