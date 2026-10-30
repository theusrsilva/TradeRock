import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Callout, Icon, Modal } from '../../components/ui'
import { ApiError, api } from '../../lib/api'
import { gameByFamily } from '../../lib/games'
import type { GameFamily, VaultPokemon } from '../../lib/types'

interface Destination {
  game: GameFamily
  refusal: string | null
}

/**
 * Mover um Pokémon para outro jogo, como o Pokémon HOME faz (Entrega 14d). O registro do cofre continua o mesmo; o
 * "Meu treinador" do jogo de destino passa a ser o treinador atual.
 */
export function TransferDialog({ pokemon, onDone, onClose }: { pokemon: VaultPokemon; onDone: (moved: VaultPokemon) => void; onClose: () => void }) {
  const { t } = useTranslation()
  const [destinations, setDestinations] = useState<Destination[] | null>(null)
  const [choice, setChoice] = useState<GameFamily | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api
      .get<Destination[]>(`/api/vault/pokemon/${pokemon.id}/destinations`)
      .then(setDestinations)
      .catch(() => setDestinations([]))
  }, [pokemon.id])

  async function move() {
    if (!choice) return
    setBusy(true)
    setError(null)
    try {
      const r = await api.post<{ pokemon: VaultPokemon; notes: string[] }>(`/api/vault/pokemon/${pokemon.id}/transfer`, { game: choice })
      onDone(r.pokemon)
    } catch (e) {
      setError(e instanceof ApiError ? (e.code ?? 'no_route') : 'no_route')
    } finally {
      setBusy(false)
    }
  }

  const others = destinations?.filter((d) => d.refusal !== 'same_game') ?? []
  return (
    <Modal
      title={t('vault.transfer.title')}
      actions={
        <>
          <Button variant="ghost" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button icon="move_up" disabled={!choice || busy} onClick={() => void move()}>
            {t('vault.transfer.confirm')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <p className="text-muted">{t('vault.transfer.help')}</p>
        {destinations === null && <p className="text-muted">{t('common.loading')}</p>}
        <div className="flex flex-col gap-1.5" role="radiogroup">
          {others.map((d) => (
            <button
              key={d.game}
              type="button"
              role="radio"
              aria-checked={choice === d.game}
              disabled={d.refusal !== null}
              onClick={() => setChoice(d.game)}
              className={`flex cursor-pointer items-center gap-2.5 rounded-lg border px-3 py-2 text-left disabled:cursor-not-allowed disabled:opacity-60 ${choice === d.game ? 'border-accent bg-accent-soft' : 'border-line'}`}
            >
              <Icon name={d.refusal ? 'block' : choice === d.game ? 'radio_button_checked' : 'radio_button_unchecked'} className="text-[20px] text-muted" />
              <span className="flex-1">
                <b>{gameByFamily(d.game).name}</b>
                {d.refusal && <span className="block text-[12.5px] text-muted">{t(`vault.transfer.refusal.${d.refusal}`, { defaultValue: d.refusal })}</span>}
              </span>
            </button>
          ))}
        </div>
        <Callout tone="amber" icon="info" className="text-[13px]">
          {t('vault.transfer.tracker')}
        </Callout>
        {error && (
          <Callout tone="red" icon="error">
            {t(`vault.transfer.refusal.${error}`, { defaultValue: t('common.error') })}
          </Callout>
        )}
      </div>
    </Modal>
  )
}
