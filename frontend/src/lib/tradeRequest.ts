// "Usar na troca" no cofre: guarda os Pokémon escolhidos até a tela de troca abrir.
let requested: string[] = []

export function requestTrade(ids: string[]) {
  requested = ids.slice(0, 6)
}

/** Pega (e limpa) os Pokémon pedidos. */
export function takeTradeRequest(): string[] {
  const ids = requested
  requested = []
  return ids
}
