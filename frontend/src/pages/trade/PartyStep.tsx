import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { UploadArea } from '../../components/UploadArea'
import { HelpTip, Icon } from '../../components/ui'
import { spriteUrl } from '../../lib/api'
import { familyOf, gameByFamily, type GameDef } from '../../lib/games'
import type { Pokemon } from '../../lib/types'

export interface PartyState {
  party: string[] // ids, na ordem do time
  offered: string[] // ids que vão para o Switch
}

export const MAX_PARTY = 6

/**
 * FRLG: time simulado de 2 a 6, com um ou mais oferecidos. Jogos novos (pokeldn, D21): cada Pokémon escolhido é uma
 * oferta, de 1 a 6, na ordem das trocas.
 */
export function partyProblems(state: PartyState, illegalBlocked = false, frlg = true): string[] {
  const problems: string[] = []
  if (frlg && state.party.length < 2) problems.push('trade.party.needTwo')
  if (state.offered.length < 1) problems.push('trade.party.needOffered')
  if (illegalBlocked) problems.push('trade.party.illegalBlocked')
  return problems
}

const displayName = (p: Pokemon) => (p.nickname || p.speciesName).toUpperCase()

export function PartyStep({
  pokemon,
  state,
  onChange,
  onReload,
  allowIllegal,
  game,
}: {
  pokemon: Pokemon[]
  state: PartyState
  onChange: (state: PartyState) => void
  onReload: () => void
  allowIllegal: boolean
  game: GameDef
}) {
  const frlg = game.family === 'frlg'
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const byId = new Map(pokemon.map((p) => [p.id, p]))
  const offeredInOrder = state.party.filter((id) => state.offered.includes(id))
  const full = state.party.length >= MAX_PARTY

  const toggleOffered = (id: string) =>
    onChange({
      ...state,
      offered: state.offered.includes(id) ? state.offered.filter((x) => x !== id) : [...state.offered, id],
    })
  const remove = (id: string) => onChange({ party: state.party.filter((x) => x !== id), offered: state.offered.filter((x) => x !== id) })
  // Nos jogos novos, todo Pokémon escolhido vai para o Switch (uma troca cada).
  const add = (id: string) =>
    !full && onChange({ party: [...state.party, id], offered: frlg ? state.offered : [...state.offered, id] })

  const fits = (p: Pokemon) => familyOf(p) === game.family
  const q = query.trim().toLowerCase()
  const list = (q
    ? pokemon.filter((p) => [p.speciesName, p.nickname, p.otName, String(p.species)].some((s) => s?.toLowerCase().includes(q)))
    : pokemon
  ).toSorted((a, b) => Number(fits(b)) - Number(fits(a)))

  const offeredIllegal = state.offered.some((id) => byId.get(id)?.legality === 'illegal')
  const checks = [
    ...(frlg ? [{ ok: state.party.length >= 2, text: t('ui.trade.atLeastTwo') }] : []),
    {
      ok: state.offered.length >= 1,
      text: state.offered.length >= 1 ? t('ui.trade.goingCount', { count: state.offered.length }) : t('trade.party.needOffered'),
    },
    ...(offeredIllegal && !allowIllegal ? [{ ok: false, text: t('trade.party.illegalBlocked') }] : []),
  ]

  return (
    <div className="grid items-start gap-[18px] [grid-template-columns:repeat(auto-fit,minmax(min(340px,100%),1fr))]">
      <div className="flex flex-col gap-3.5 rounded-xl border border-line bg-surface p-5">
        <div className="flex items-center gap-2">
          <div className="flex-1 text-[17px] font-bold">
            {t('trade.party.title')} <span className="font-medium text-muted">· {state.party.length}/6</span>
          </div>
          <HelpTip>{frlg ? t('ui.trade.teamTip') : t('ui.trade.offersTip')}</HelpTip>
        </div>
        <div className="grid grid-cols-3 gap-2.5">
          {Array.from({ length: MAX_PARTY }, (_, i) => {
            const id = state.party[i]
            const p = id ? byId.get(id) : undefined
            if (!p)
              return (
                <div key={i} className="flex min-h-[150px] flex-col items-center justify-center gap-1 rounded-lg border-[1.5px] border-dashed border-line text-muted">
                  <Icon name="add" className="text-[26px] opacity-60" />
                  <span className="text-xs">{t('ui.trade.empty')}</span>
                </div>
              )
            const offered = state.offered.includes(p.id)
            const blocked = offered && p.legality === 'illegal' && !allowIllegal
            const warn = p.warnings?.[0] ? t(`warnings.${p.warnings[0].code}`, p.warnings[0].args) : p.legality === 'illegal' ? t('legality.illegal') : null
            const order = offeredInOrder.indexOf(p.id) + 1
            return (
              <div
                key={p.id}
                className={`relative flex min-h-[150px] flex-col items-center justify-center gap-1 rounded-lg border-[1.5px] px-1.5 py-2.5 ${blocked ? 'border-bad bg-bad-soft' : offered ? 'border-accent bg-accent-soft' : 'border-line bg-tile'}`}
              >
                <button
                  type="button"
                  onClick={() => remove(p.id)}
                  title={t('trade.party.remove')}
                  aria-label={t('trade.party.remove')}
                  className="absolute top-1 right-1 flex h-6 w-6 cursor-pointer items-center justify-center rounded-full text-muted hover:bg-surface2"
                >
                  <Icon name="close" className="text-[18px]" />
                </button>
                {warn && (
                  <span title={warn} className="absolute top-1.5 left-2 text-warn">
                    <Icon name="warning" className="text-[18px]" />
                  </span>
                )}
                <img src={spriteUrl(p.species)} alt="" className="pixelated h-16 w-16" loading="lazy" />
                <div className="max-w-full truncate text-[13px] font-bold">{displayName(p)}</div>
                {frlg ? (
                  <button
                    type="button"
                    onClick={() => toggleOffered(p.id)}
                    aria-pressed={offered}
                    title={blocked ? t('trade.party.illegalBlocked') : undefined}
                    className={`flex h-7 cursor-pointer items-center gap-1 rounded-full border px-2.5 text-xs font-bold whitespace-nowrap ${
                      offered ? 'border-accent bg-accent text-accent-ink' : 'border-line bg-surface text-muted'
                    }`}
                  >
                    <Icon name={offered ? 'north_east' : 'home'} className="text-[15px]" />
                    {offered ? `${t('ui.trade.send')}${offeredInOrder.length > 1 ? ` · ${order}` : ''}` : t('ui.trade.stays')}
                  </button>
                ) : (
                  <span className="flex h-7 items-center gap-1 rounded-full bg-accent px-2.5 text-xs font-bold whitespace-nowrap text-accent-ink">
                    <Icon name="north_east" className="text-[15px]" />
                    {t('ui.trade.tradeNumber', { n: order })}
                  </span>
                )}
              </div>
            )
          })}
        </div>
        <div className="flex flex-col gap-1.5">
          {checks.map((c) => (
            <div key={c.text} className={`flex items-center gap-2 text-[13.5px] ${c.ok ? 'text-ok' : 'text-muted'}`}>
              <Icon name={c.ok ? 'check_circle' : 'radio_button_unchecked'} className="text-[18px]" />
              {c.text}
            </div>
          ))}
        </div>
      </div>

      <div className="flex flex-col gap-3.5 rounded-xl border border-line bg-surface p-5">
        <div className="flex flex-wrap items-center gap-2.5">
          <div className="flex-1 text-[17px] font-bold">{t('ui.trade.fromVault')}</div>
          <UploadArea compact onImported={onReload} />
        </div>
        <div className="flex h-10 items-center gap-2 rounded-lg border border-line bg-bg px-3 focus-within:border-accent">
          <Icon name="search" className="text-muted" />
          <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t('ui.trade.searchVault')} className="min-w-0 flex-1 bg-transparent text-sm outline-none" />
        </div>
        {full && <p className="text-[13px] text-warn">{t('trade.party.full')}</p>}
        <div className="grid max-h-[430px] gap-2 overflow-auto [grid-template-columns:repeat(auto-fill,minmax(92px,1fr))]">
          {list.map((p) => {
            const inTeam = state.party.includes(p.id)
            const warn = p.warnings?.[0] ? t(`warnings.${p.warnings[0].code}`, p.warnings[0].args) : null
            // Pokémon de outro jogo aparece esmaecido, com o motivo (dá para mover no cofre).
            const other = fits(p) ? null : t('ui.trade.otherGame', { game: gameByFamily(familyOf(p)).name })
            return (
              <button
                key={p.id}
                type="button"
                onClick={() => (inTeam ? remove(p.id) : add(p.id))}
                disabled={(!inTeam && full) || (other !== null && !inTeam)}
                title={other ?? `${displayName(p)} · ${t('pokemon.level', { level: p.level })}${warn ? ` · ${warn}` : ''}`}
                aria-pressed={inTeam}
                className={`relative flex cursor-pointer flex-col items-center gap-0.5 rounded-lg border bg-tile px-1 py-2 disabled:cursor-not-allowed disabled:opacity-45 ${inTeam ? 'border-accent' : 'border-line'}`}
              >
                {inTeam && (
                  <span className="absolute top-1 right-1 flex h-[18px] w-[18px] items-center justify-center rounded-full bg-accent text-accent-ink">
                    <Icon name="check" className="text-[14px]" />
                  </span>
                )}
                {(warn || p.legality === 'illegal') && (
                  <span className="absolute top-1 left-1.5 text-warn">
                    <Icon name="warning" className="text-base" />
                  </span>
                )}
                <img src={spriteUrl(p.species)} alt="" className="pixelated h-14 w-14" loading="lazy" />
                <span className="max-w-full truncate text-[11.5px] font-bold">{displayName(p)}</span>
                <span className="text-[11px] text-muted">{p.isEgg ? t('pokemon.egg') : t('ui.lv', { level: p.level })}</span>
              </button>
            )
          })}
          {list.length === 0 && <p className="col-span-full py-6 text-center text-sm text-muted">{t('ui.vault.nothingHere')}</p>}
        </div>
      </div>
    </div>
  )
}
