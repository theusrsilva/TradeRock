// O cookie de sessão (HttpOnly) vai automaticamente; em dev o proxy do Vite injeta o token.
export async function apiGet<T>(path: string): Promise<T> {
  const response = await fetch(path, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
  if (!response.ok) throw new Error(`HTTP ${response.status}`)
  return (await response.json()) as T
}

export interface Health {
  app: string
  version: string
  os: 'linux' | 'windows' | 'other'
  environment: string
}
