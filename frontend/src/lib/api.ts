// Cliente da API. O cookie de sessão (HttpOnly) vai automaticamente; em dev o proxy do Vite injeta o token.
// Pela rede (modo servidor), toda alteração leva também o token anti-CSRF recebido no login.

let csrfToken: string | null = null

export const unauthorizedEvent = 'traderock:unauthorized'

export function setCsrfToken(token: string | null) {
  csrfToken = token
}

/** Cabeçalhos extras para requisições que alteram algo (POST/PUT/DELETE). */
export function csrfHeaders(): Record<string, string> {
  return csrfToken ? { 'X-TradeRock-CSRF': csrfToken } : {}
}

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string | null,
    public errors: string[],
  ) {
    super(`HTTP ${status}${code ? ` (${code})` : ''}`)
  }
}

async function request<T>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  const init: RequestInit = {
    method,
    credentials: 'same-origin',
    headers: { Accept: 'application/json', ...(method === 'GET' ? {} : csrfHeaders()) },
    signal,
  }
  if (body instanceof FormData) {
    init.body = body
  } else if (body !== undefined) {
    init.body = JSON.stringify(body)
    init.headers = { ...init.headers, 'Content-Type': 'application/json' }
  }

  const response = await fetch(path, init)
  if (!response.ok) {
    let code: string | null = null
    let errors: string[] = []
    try {
      const data = (await response.json()) as { error?: string; errors?: string[] }
      code = data.error ?? null
      errors = data.errors ?? []
    } catch {
      // corpo vazio ou não-JSON
    }
    // Pela rede, o login expirou ou caiu (senha trocada, modo servidor desligado): volta à tela de entrada.
    if (response.status === 401 && code === 'login_required') window.dispatchEvent(new Event(unauthorizedEvent))
    throw new ApiError(response.status, code, errors)
  }
  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown, signal?: AbortSignal) => request<T>('POST', path, body, signal),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  delete: <T>(path: string) => request<T>('DELETE', path),
}

export const spriteUrl = (species: number) => `/api/sprites/${species}`
