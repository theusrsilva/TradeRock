import { useEffect, useRef } from 'react'

/** Roda <effect> depois de <delay> ms sem mudanças em <deps>. Descarta respostas antigas via o contador. */
export function useDebouncedEffect(effect: (isCurrent: () => boolean) => void, deps: unknown[], delay = 400) {
  const counter = useRef(0)
  useEffect(() => {
    const id = ++counter.current
    const timer = setTimeout(() => effect(() => id === counter.current), delay)
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)
}
