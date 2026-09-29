// Arrastar e soltar dentro do cofre (HTML5 drag and drop).
export const DRAG_TYPE = 'application/x-traderock-pokemon'

export function startDrag(e: React.DragEvent, pokemonId: string) {
  e.dataTransfer.setData(DRAG_TYPE, pokemonId)
  e.dataTransfer.effectAllowed = 'move'
}

export function draggedPokemon(e: React.DragEvent): string | null {
  return e.dataTransfer.types.includes(DRAG_TYPE) ? e.dataTransfer.getData(DRAG_TYPE) || null : null
}

export function acceptsPokemon(e: React.DragEvent): boolean {
  return e.dataTransfer.types.includes(DRAG_TYPE)
}
