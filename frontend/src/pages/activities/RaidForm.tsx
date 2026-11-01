import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Field, inputClass, Segmented, selectClass, Toggle } from '../../components/ui'
import { SearchSelect } from '../../components/SearchSelect'
import { api } from '../../lib/api'
import { useBuilderData } from '../../lib/builderData'
import type { GameFamily, Pokemon } from '../../lib/types'

/** Tipos de raid (pokeldn_trade.py RAIDS): "host" = o console entra na sala do TradeRock; "join" = o contrário. */
export type RaidKind = 'max-raid-host' | 'max-raid-join' | 'lair-host' | 'lair-join' | 'tera-raid-host' | 'tera-raid-join'

export const RAID_KINDS: Record<'swsh' | 'sv', RaidKind[]> = {
  swsh: ['max-raid-host', 'max-raid-join', 'lair-host', 'lair-join'],
  sv: ['tera-raid-host', 'tera-raid-join'],
}

export interface RaidState {
  kind: RaidKind
  boss?: string
  offer?: string
  pokemon?: string
  event?: string
  eventTitle?: string
  eventBoss?: string
  gigantamax?: boolean
  /** 1 a 4, ou 0 = a mini IA escolhe (D31). */
  move?: number
  /** Max Raid em que o TradeRock entra: o chefe, para a mini IA escolher o golpe. */
  targetSpecies?: number
  legendary?: number
  code?: string
  version?: string
  map?: string
  progress?: string
  content?: string
  seed?: string
  group?: number
  players?: number
}

/** catalog/raids.json (GET /api/activities/raid-events). */
export interface RaidEvents {
  swsh: { key: string; title: string; bosses: [string, string][] }[]
  sv: {
    key: string
    title: string
    species: string[]
    versions: Record<string, { group: number; label: string; content: string; progress: string[] }[]>
  }[]
}

const LAIR_NAMES: Record<number, string> = {
  144: 'Articuno', 145: 'Zapdos', 146: 'Moltres', 150: 'Mewtwo', 243: 'Raikou', 244: 'Entei', 245: 'Suicune',
  249: 'Lugia', 250: 'Ho-Oh', 380: 'Latias', 381: 'Latios', 382: 'Kyogre',
}
const SV_VERSIONS = ['scarlet', 'violet']
const SV_MAPS = ['paldea', 'kitakami', 'blueberry']
const SV_PROGRESS = ['beginning', 'tera', '3star', '4star', '5star', '6star']

export function newRaid(kind: RaidKind): RaidState {
  if (kind === 'tera-raid-host') return { kind, version: 'violet', map: 'paldea', progress: '4star', content: 'standard', seed: '000F34C3', players: 1 }
  if (kind === 'lair-host') return { kind, legendary: 150 }
  if (kind === 'max-raid-join') return { kind, move: 0 }
  return { kind }
}

/** A raid tem tudo o que o pokeldn exige? */
export function raidReady(r: RaidState): boolean {
  switch (r.kind) {
    case 'max-raid-host':
      return !!r.offer && (r.event ? !!r.eventBoss : !!r.boss)
    case 'tera-raid-host':
      return !!r.pokemon && /^[0-9a-fA-F]{8}$/.test(r.seed ?? '') && (!r.event || !!r.progress)
    case 'tera-raid-join':
      return !!r.pokemon
    case 'lair-host':
      return !!r.legendary && (!r.code || /^\d{8}$/.test(r.code))
    default:
      return true
  }
}

export function RaidForm({
  family,
  raid,
  onChange,
  pokemon,
}: {
  family: 'swsh' | 'sv'
  raid: RaidState
  onChange: (raid: RaidState) => void
  pokemon: Pokemon[]
}) {
  const { t } = useTranslation()
  const [events, setEvents] = useState<RaidEvents | null>(null)
  const swshData = useBuilderData('sword')
  const set = (patch: Partial<RaidState>) => onChange({ ...raid, ...patch })
  const mine = pokemon.filter((p) => p.family === (family as GameFamily))
  const usesEvents = raid.kind === 'max-raid-host' || raid.kind === 'tera-raid-host'

  useEffect(() => {
    if (usesEvents && !events) api.get<RaidEvents>('/api/activities/raid-events').then(setEvents, () => {})
  }, [usesEvents, events])

  const picker = (value: string | undefined, onPick: (id: string | undefined) => void, optional = false) => (
    <select className={selectClass} value={value ?? ''} onChange={(e) => onPick(e.target.value || undefined)}>
      <option value="">{optional ? t('activities.raid.anyPokemon') : t('activities.raid.choosePokemon')}</option>
      {mine.map((p) => (
        <option key={p.id} value={p.id}>
          {(p.nickname || p.speciesName) + ` · ${t('activities.raid.level', { level: p.level })}`}
        </option>
      ))}
    </select>
  )

  return (
    <div className="flex flex-col gap-4">
      <Segmented<RaidKind>
        value={raid.kind}
        onChange={(k) => onChange(newRaid(k))}
        options={RAID_KINDS[family].map((k) => ({ value: k, label: t(`activities.raid.kinds.${k}`) }))}
      />
      <p className="text-sm text-muted">{t(`activities.raid.about.${raid.kind}`)}</p>
      {mine.length === 0 && raid.kind !== 'lair-host' && raid.kind !== 'lair-join' && (
        <p className="text-sm text-warn">{t('activities.raid.noPokemon')}</p>
      )}

      {raid.kind === 'max-raid-host' && (
        <SwshHost raid={raid} set={set} events={events} picker={picker} />
      )}
      {raid.kind === 'max-raid-join' && (
        <>
          <Field label={t('activities.raid.offer')}>{picker(raid.offer, (offer) => set({ offer }), true)}</Field>
          <Field label={t('activities.raid.move')} help={raid.move === 0 ? t('activities.raid.moveAutoHelp') : undefined}>
            <select className={selectClass} value={raid.move ?? 0} onChange={(e) => set({ move: Number(e.target.value) })}>
              <option value={0}>{t('activities.raid.moveAuto')}</option>
              {[1, 2, 3, 4].map((m) => (
                <option key={m} value={m}>
                  {t('activities.raid.moveN', { n: m })}
                </option>
              ))}
            </select>
          </Field>
          {raid.move === 0 && (
            <Field label={t('activities.raid.target')} help={t('activities.raid.targetHelp')}>
              <SearchSelect
                options={(swshData?.species ?? []).map((s) => ({ value: s.value, text: `#${s.value} ${s.text}` }))}
                value={raid.targetSpecies ?? null}
                onChange={(v) => set({ targetSpecies: v ?? undefined })}
                placeholder={t('builder.searchSpecies')}
                emptyLabel={t('activities.raid.targetUnknown')}
              />
            </Field>
          )}
        </>
      )}
      {raid.kind === 'lair-host' && (
        <>
          <Field label={t('activities.raid.legendary')}>
            <select className={selectClass} value={raid.legendary} onChange={(e) => set({ legendary: Number(e.target.value) })}>
              {Object.entries(LAIR_NAMES).map(([n, name]) => (
                <option key={n} value={n}>
                  {name}
                </option>
              ))}
            </select>
          </Field>
          <Field label={t('activities.raid.code')} help={t('activities.raid.codeHelp')}>
            <input className={inputClass} inputMode="numeric" maxLength={8} value={raid.code ?? ''} onChange={(e) => set({ code: e.target.value.replace(/\D/g, '') || undefined })} />
          </Field>
        </>
      )}
      {raid.kind === 'tera-raid-host' && <SvHost raid={raid} set={set} events={events} picker={picker} />}
      {raid.kind === 'tera-raid-join' && (
        <Field label={t('activities.raid.svOffer')}>{picker(raid.pokemon, (p) => set({ pokemon: p }))}</Field>
      )}
    </div>
  )
}

type Picker = (value: string | undefined, onPick: (id: string | undefined) => void, optional?: boolean) => React.ReactNode

function SwshHost({ raid, set, events, picker }: { raid: RaidState; set: (p: Partial<RaidState>) => void; events: RaidEvents | null; picker: Picker }) {
  const { t } = useTranslation()
  const fromEvent = raid.event !== undefined
  const event = events?.swsh.find((e) => e.key === raid.event)
  return (
    <>
      <Segmented<'vault' | 'event'>
        value={fromEvent ? 'event' : 'vault'}
        onChange={(v) => set(v === 'event' ? { event: events?.swsh.at(-1)?.key ?? '', eventTitle: events?.swsh.at(-1)?.title, eventBoss: undefined, boss: undefined } : { event: undefined, eventTitle: undefined, eventBoss: undefined })}
        options={[
          { value: 'vault', label: t('activities.raid.bossVault') },
          { value: 'event', label: t('activities.raid.bossEvent') },
        ]}
      />
      {!fromEvent && (
        <>
          <Field label={t('activities.raid.boss')}>{picker(raid.boss, (boss) => set({ boss }))}</Field>
          <Toggle on={!!raid.gigantamax} onChange={(gigantamax) => set({ gigantamax })} label={t('activities.raid.gigantamax')} />
        </>
      )}
      {fromEvent && (
        <>
          <Field label={t('activities.raid.event')}>
            <select className={selectClass} value={raid.event} onChange={(e) => set({ event: e.target.value, eventTitle: events?.swsh.find((x) => x.key === e.target.value)?.title, eventBoss: undefined })}>
              {[...(events?.swsh ?? [])].reverse().map((e) => (
                <option key={e.key} value={e.key}>
                  {e.title}
                </option>
              ))}
            </select>
          </Field>
          <Field label={t('activities.raid.boss')}>
            <select className={selectClass} value={raid.eventBoss ?? ''} onChange={(e) => set({ eventBoss: e.target.value || undefined })}>
              <option value="">{t('activities.raid.chooseBoss')}</option>
              {event?.bosses.map(([value, label]) => {
                const [version, stars, , area] = value.split(':')
                return (
                  <option key={value} value={value}>
                    {`${'★'.repeat(Number(stars))} ${label} · ${version === 'sword' ? 'Sword' : 'Shield'} · ${area}`}
                  </option>
                )
              })}
            </select>
          </Field>
        </>
      )}
      <Field label={t('activities.raid.offer')} help={t('activities.raid.offerHelp')}>
        {picker(raid.offer, (offer) => set({ offer }))}
      </Field>
    </>
  )
}

function SvHost({ raid, set, events, picker }: { raid: RaidState; set: (p: Partial<RaidState>) => void; events: RaidEvents | null; picker: Picker }) {
  const { t } = useTranslation()
  const event = events?.sv.find((e) => e.key === raid.event)
  const versions = event ? Object.keys(event.versions) : SV_VERSIONS
  const groups = event && raid.version ? (event.versions[raid.version] ?? []) : []
  const group = groups.find((g) => g.group === raid.group) ?? groups[0]
  const progresses = event ? (group?.progress ?? []) : SV_PROGRESS
  const sorted = useMemo(() => [...(events?.sv ?? [])].reverse(), [events])

  // Com evento, versão, grupo e etapa ficam entre os que o evento tem.
  useEffect(() => {
    if (!event) return
    const version = versions.includes(raid.version ?? '') ? raid.version! : versions[0]
    const gs = event.versions[version] ?? []
    const g = gs.find((x) => x.group === raid.group) ?? gs[0]
    const progress = g?.progress.includes(raid.progress ?? '') ? raid.progress : g?.progress.at(-1)
    if (version !== raid.version || g?.group !== raid.group || progress !== raid.progress) set({ version, group: g?.group, progress })
  }, [event, raid.version, raid.group, raid.progress]) // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <>
      <Field label={t('activities.raid.svBoss')} help={t('activities.raid.svBossHelp')}>
        {picker(raid.pokemon, (p) => set({ pokemon: p }))}
      </Field>
      <Field label={t('activities.raid.event')} help={t('activities.raid.svEventHelp')}>
        <select
          className={selectClass}
          value={raid.event ?? ''}
          onChange={(e) => set({ event: e.target.value || undefined, eventTitle: sorted.find((x) => x.key === e.target.value)?.title, group: undefined })}
        >
          <option value="">{t('activities.raid.noEvent')}</option>
          {sorted.map((e) => (
            <option key={e.key} value={e.key}>
              {e.title}
            </option>
          ))}
        </select>
      </Field>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label={t('activities.raid.version')}>
          <select className={selectClass} value={raid.version} onChange={(e) => set({ version: e.target.value })}>
            {versions.map((v) => (
              <option key={v} value={v}>
                {v === 'scarlet' ? 'Scarlet' : 'Violet'}
              </option>
            ))}
          </select>
        </Field>
        {event ? (
          <Field label={t('activities.raid.group')}>
            <select className={selectClass} value={group?.group ?? ''} onChange={(e) => set({ group: Number(e.target.value) })}>
              {groups.map((g) => (
                <option key={g.group} value={g.group}>
                  {g.label}
                </option>
              ))}
            </select>
          </Field>
        ) : (
          <Field label={t('activities.raid.map')}>
            <select className={selectClass} value={raid.map} onChange={(e) => set({ map: e.target.value })}>
              {SV_MAPS.map((m) => (
                <option key={m} value={m}>
                  {t(`activities.raid.maps.${m}`)}
                </option>
              ))}
            </select>
          </Field>
        )}
        <Field label={t('activities.raid.progress')}>
          <select className={selectClass} value={raid.progress} onChange={(e) => set({ progress: e.target.value })}>
            {progresses.map((p) => (
              <option key={p} value={p}>
                {t(`activities.raid.progressNames.${p}`)}
              </option>
            ))}
          </select>
        </Field>
        <Field label={t('activities.raid.players')} help={t('activities.raid.playersHelp')}>
          <select className={selectClass} value={raid.players ?? 1} onChange={(e) => set({ players: Number(e.target.value) })}>
            {[1, 2, 3].map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </Field>
      </div>
      {!event && (
        <Toggle on={raid.content === 'black'} onChange={(on) => set({ content: on ? 'black' : 'standard' })} label={t('activities.raid.black')} />
      )}
      <Field label={t('activities.raid.seed')} help={t('activities.raid.seedHelp')}>
        <input className={inputClass} maxLength={8} value={raid.seed ?? ''} onChange={(e) => set({ seed: e.target.value.replace(/[^0-9a-fA-F]/g, '').toUpperCase() })} />
      </Field>
    </>
  )
}
