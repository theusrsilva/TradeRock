// Arquivos soltos na janela (fora do cofre) esperando a página do cofre abrir.
let pendingSave: File | null = null

export function setPendingSave(file: File) {
  pendingSave = file
}

export function takePendingSave(): File | null {
  const file = pendingSave
  pendingSave = null
  return file
}

export const VAULT_CHANGED = 'traderock:vault-changed'

export function notifyVaultChanged() {
  window.dispatchEvent(new Event(VAULT_CHANGED))
}
