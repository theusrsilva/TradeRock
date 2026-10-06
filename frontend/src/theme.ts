import { useEffect, useSyncExternalStore } from 'react'

export type ThemeChoice = 'system' | 'light' | 'dark'

const storageKey = 'traderock.theme'

export function resolveTheme(choice: ThemeChoice, systemPrefersDark: boolean): 'light' | 'dark' {
  if (choice === 'system') return systemPrefersDark ? 'dark' : 'light'
  return choice
}

function readChoice(): ThemeChoice {
  try {
    const saved = localStorage.getItem(storageKey)
    if (saved === 'light' || saved === 'dark' || saved === 'system') return saved
  } catch {
    // ignora
  }
  return 'system'
}

// Escolha compartilhada entre o menu (botão claro/escuro) e Configurações → Preferências.
let current: ThemeChoice = typeof window === 'undefined' ? 'system' : readChoice()
const listeners = new Set<() => void>()

function setChoice(choice: ThemeChoice) {
  current = choice
  try {
    localStorage.setItem(storageKey, choice)
  } catch {
    // ignora
  }
  listeners.forEach((l) => l())
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

const media = () => window.matchMedia('(prefers-color-scheme: dark)')

function subscribeSystem(listener: () => void) {
  const m = media()
  m.addEventListener('change', listener)
  return () => m.removeEventListener('change', listener)
}

/** Tema escolhido, o tema em uso (claro/escuro) e a função para trocar. Aplica a classe `dark` no <html>. */
export function useTheme() {
  const choice = useSyncExternalStore(subscribe, () => current, () => current)
  const systemDark = useSyncExternalStore(subscribeSystem, () => media().matches, () => false)
  const theme = resolveTheme(choice, systemDark)

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark')
    document.documentElement.style.colorScheme = theme
  }, [theme])

  return [choice, setChoice, theme] as const
}
