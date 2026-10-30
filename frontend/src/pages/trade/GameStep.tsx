import { useTranslation } from 'react-i18next'
import { Badge, Icon } from '../../components/ui'
import { spriteUrl } from '../../lib/api'
import { GAMES, gameOf, type GameDef } from '../../lib/games'
import type { TradeGame } from '../../lib/types'

/**
 * Primeiro passo da troca: o jogo e a versão. Cada motor diz os jogos que troca (o ESP32 com o pokeldn, todos;
 * a placa Wi-Fi, por enquanto, só o FRLG). No modo demonstração, todos.
 */
export function GameStep({
  game,
  onChange,
  available,
}: {
  game: TradeGame
  onChange: (game: TradeGame) => void
  available: (game: GameDef) => boolean
}) {
  const { t } = useTranslation()
  const current = gameOf(game)

  return (
    <div className="flex flex-col gap-4 rounded-xl border border-line bg-surface p-[22px]">
      <div>
        <div className="text-lg font-bold">{t('ui.trade.whichGame')}</div>
        <div className="text-sm text-muted">{t('ui.trade.whichGameSub')}</div>
      </div>
      <div className="grid gap-3 [grid-template-columns:repeat(auto-fill,minmax(min(250px,100%),1fr))]" role="radiogroup">
        {GAMES.map((g) => {
          const on = current.family === g.family
          const ok = available(g)
          return (
            <div
              key={g.family}
              className={`flex flex-col gap-2.5 rounded-xl border-2 p-3.5 ${on ? 'border-accent bg-accent-soft' : 'border-line bg-bg'} ${ok ? '' : 'opacity-60'}`}
            >
              <button
                type="button"
                role="radio"
                aria-checked={on}
                disabled={!ok}
                onClick={() => onChange(g.versions[0].id)}
                className="flex cursor-pointer items-center gap-3 text-left disabled:cursor-not-allowed"
              >
                <img src={spriteUrl(g.icon)} alt="" className="pixelated h-12 w-12" loading="lazy" />
                <span className="flex-1 text-base leading-tight font-bold">{g.name}</span>
                {on && <Icon name="check_circle" className="text-[22px] text-accent" />}
              </button>
              {g.versions.length > 1 && (
                <div className="flex flex-wrap gap-1.5">
                  {g.versions.map((v) => (
                    <button
                      key={v.id}
                      type="button"
                      disabled={!ok}
                      aria-pressed={game === v.id}
                      onClick={() => onChange(v.id)}
                      className={`flex h-8 cursor-pointer items-center gap-1.5 rounded-full border px-3 text-[13px] font-semibold disabled:cursor-not-allowed ${game === v.id ? 'border-accent bg-surface' : 'border-line'}`}
                    >
                      <span className="h-2.5 w-2.5 rounded-full" style={{ background: v.color }} />
                      {v.name}
                    </button>
                  ))}
                </div>
              )}
              <div className="flex flex-wrap gap-1.5">
                {g.linkCode !== 'none' && <Badge>{t('ui.trade.games.linkCode')}</Badge>}
                {!ok && <Badge tone="amber">{t('ui.trade.games.soon')}</Badge>}
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
