import { useEffect, useState } from 'react'

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

export function useTheme() {
  const [choice, setChoice] = useState<ThemeChoice>(readChoice)

  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const apply = () => {
      const theme = resolveTheme(choice, media.matches)
      document.documentElement.classList.toggle('dark', theme === 'dark')
      document.documentElement.style.colorScheme = theme
    }
    apply()
    media.addEventListener('change', apply)
    try {
      localStorage.setItem(storageKey, choice)
    } catch {
      // ignora
    }
    return () => media.removeEventListener('change', apply)
  }, [choice])

  return [choice, setChoice] as const
}
