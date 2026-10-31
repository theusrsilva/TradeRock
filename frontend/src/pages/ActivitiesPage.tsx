import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ContentFile } from '../components/Markdown'
import { Badge, Button, Callout, Icon, inputClass, PageTitle, Segmented } from '../components/ui'
import { api, ApiError } from '../lib/api'
import { GAMES, gameOf, versionName } from '../lib/games'
import { giftText, groupName, whenText } from '../lib/gifts'
import type { ActivitiesCatalog, ActivitySnapshot, GiftEntry, Pokemon, TradeGame } from '../lib/types'
import { CircleForm, circleReady, newCircle, type CircleState } from './activities/CircleForm'
import { newRaid, RAID_KINDS, RaidForm, raidReady, type RaidState } from './activities/RaidForm'
import { GameStep } from './trade/GameStep'

type Activity = 'gift' | 'raid' | 'circle'

type Mode = 'preset' | 'event' | 'file'

interface GiftFile {
  name: string
  extension: string
  data: string
}

/**
 * Outras atividades (D26): Mystery Gift (FireRed/LeafGreen e Sword/Shield), raids (Sword/Shield e Scarlet/Violet) e
 * Union Circle (Scarlet/Violet),
 * pelo rádio configurado (ESP32 ou placa Wi-Fi; as raids do Sword/Shield também pela rede de casa) ou simuladas no
 * modo demonstração.
 */
export default function ActivitiesPage() {
  const { t } = useTranslation()
  const [catalog, setCatalog] = useState<ActivitiesCatalog | null>(null)
  const [current, setCurrent] = useState<ActivitySnapshot | null>(null)
  const [game, setGame] = useState<TradeGame>('fire_red')
  const [mode, setMode] = useState<Mode>('preset')
  const [key, setKey] = useState<string | null>(null)
  const [file, setFile] = useState<GiftFile | null>(null)
  const [search, setSearch] = useState('')
  const [errors, setErrors] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const [activity, setActivity] = useState<Activity>('gift')
  const [raid, setRaid] = useState<RaidState>(newRaid('max-raid-host'))
  const [vault, setVault] = useState<Pokemon[]>([])
  const [circle, setCircle] = useState<CircleState>(newCircle())

  useEffect(() => {
    api.get<ActivitiesCatalog>('/api/activities').then(setCatalog, () => setErrors(['load_failed']))
    api.get<Pokemon[]>('/api/pokemon').then(setVault, () => {})
    api.get<ActivitySnapshot | null>('/api/activities/current').then((s) => setCurrent(s ?? null), () => {})
  }, [])

  // Acompanha a atividade em andamento.
  const running = current?.status === 'running'
  useEffect(() => {
    if (!running) return
    const timer = window.setInterval(() => {
      api.get<ActivitySnapshot | null>('/api/activities/current').then((s) => setCurrent(s ?? null), () => {})
    }, 1000)
    return () => window.clearInterval(timer)
  }, [running])

  const family = gameOf(game).family
  const gifts = catalog?.gifts[family]
  const games = GAMES.filter((g) => catalog?.games.some((a) => a.game === g.family))
  const gameActivities = catalog?.games.find((g) => g.game === family)
  const extensions = gameActivities?.giftExtensions ?? []
  const offered = (gameActivities?.activities ?? []) as Activity[]
  const supported = (gameActivities?.supported ?? []) as Activity[]

  const chooseGame = (g: TradeGame) => {
    setGame(g)
    const next = gameOf(g).family
    if (next !== family) {
      setKey(null)
      setFile(null)
      setMode('preset')
      setSearch('')
      const kinds = catalog?.games.find((x) => x.game === next)?.activities ?? []
      setActivity(kinds.includes('gift') ? 'gift' : 'raid')
      if (next === 'swsh' || next === 'sv') setRaid(newRaid(RAID_KINDS[next][0]))
    }
  }

  const list: GiftEntry[] = mode === 'event' ? (gifts?.events ?? []) : (gifts?.presets ?? [])
  const shown = useMemo(() => {
    const q = search.trim().toLowerCase()
    return list.filter((g) => !q || `${g.label} ${g.summary} ${giftText(family, g).label}`.toLowerCase().includes(q))
  }, [list, search, family])
  const groups = useMemo(() => {
    const out = new Map<string, GiftEntry[]>()
    for (const g of shown) out.set(g.group, [...(out.get(g.group) ?? []), g])
    return [...out.entries()]
  }, [shown])

  const chosen = mode === 'file' ? file !== null : list.some((g) => g.key === key)
  const chosenEntry = list.find((g) => g.key === key)

  const openFile = async (f: File | undefined) => {
    setErrors([])
    if (!f) return
    const extension = f.name.split('.').pop()?.toLowerCase() ?? ''
    if (!extensions.includes(extension)) {
      setFile(null)
      setErrors(['gift_file_wrong_type'])
      return
    }
    const bytes = new Uint8Array(await f.arrayBuffer())
    let binary = ''
    for (const b of bytes) binary += String.fromCharCode(b)
    setFile({ name: f.name, extension, data: btoa(binary) })
  }

  const start = async () => {
    setBusy(true)
    setErrors([])
    try {
      if (activity === 'raid') {
        setCurrent(await api.post<ActivitySnapshot>('/api/activities', { game, kind: 'raid', raid }))
        return
      }
      if (activity === 'circle') {
        setCurrent(await api.post<ActivitySnapshot>('/api/activities', { game, kind: 'circle', circle }))
        return
      }
      const gift =
        mode === 'file' && file
          ? { mode, data: file.data, extension: file.extension, fileName: file.name }
          : { mode, key }
      setCurrent(await api.post<ActivitySnapshot>('/api/activities', { game, kind: 'gift', gift }))
    } catch (e) {
      setErrors(e instanceof ApiError ? (e.errors.length ? e.errors : [e.code ?? 'unknown']) : ['unknown'])
    } finally {
      setBusy(false)
    }
  }

  const stop = async () => {
    await api.post('/api/activities/current/cancel').catch(() => {})
  }

  if (current && showing(current)) {
    return <Progress snapshot={current} onStop={stop} onAgain={() => setCurrent(null)} />
  }

  return (
    <div className="flex flex-col gap-5">
      <PageTitle>{t('activities.title')}</PageTitle>
      <p className="-mt-2 text-sm text-muted">{t('activities.sub')}</p>

      {catalog && (!catalog.engineSupports || (gameActivities && !supported.includes(activity))) && (
        <Callout tone="amber" icon="info">
          {t('activities.engineUnsupported')}
        </Callout>
      )}

      <GameStep
        game={game}
        onChange={chooseGame}
        available={() => true}
        games={games}
        title={t('activities.whichGame')}
        sub={t('activities.whichGameSub')}
      />

      {offered.length > 1 && (
        <Segmented<Activity>
          className="self-start"
          value={activity}
          onChange={setActivity}
          options={offered.map((a) => ({ value: a, label: t(`activities.kinds.${a}`) }))}
        />
      )}

      {activity === 'raid' && (family === 'swsh' || family === 'sv') && (
        <div className="flex flex-col gap-4 rounded-xl border border-line bg-surface p-[22px]">
          <div>
            <div className="text-lg font-bold">{t('activities.raid.title')}</div>
            <div className="text-sm text-muted">{t('activities.raid.sub')}</div>
          </div>
          <RaidForm family={family} raid={raid} onChange={setRaid} pokemon={vault} />
        </div>
      )}

      {activity === 'circle' && (
        <div className="flex flex-col gap-4 rounded-xl border border-line bg-surface p-[22px]">
          <div>
            <div className="text-lg font-bold">{t('activities.circle.title')}</div>
            <div className="text-sm text-muted">{t('activities.circle.sub')}</div>
          </div>
          <CircleForm circle={circle} onChange={setCircle} />
        </div>
      )}

      {activity === 'gift' && (
      <div className="flex flex-col gap-4 rounded-xl border border-line bg-surface p-[22px]">
        <div>
          <div className="text-lg font-bold">{t('activities.gift.title')}</div>
          <div className="text-sm text-muted">{t('activities.gift.sub')}</div>
        </div>
        <Segmented<Mode>
          value={mode}
          onChange={(m) => {
            setMode(m)
            setKey(null)
            setSearch('')
          }}
          options={[
            { value: 'preset', label: t('activities.gift.modes.preset') },
            ...(gifts?.events.length ? [{ value: 'event' as Mode, label: t('activities.gift.modes.event') }] : []),
            { value: 'file', label: t('activities.gift.modes.file') },
          ]}
        />

        {mode !== 'file' && (
          <>
            <input
              className={inputClass}
              placeholder={t('activities.gift.search')}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <div className="flex max-h-[460px] flex-col gap-4 overflow-y-auto pr-1" role="radiogroup">
              {groups.map(([group, entries]) => (
                <div key={group} className="flex flex-col gap-1.5">
                  <div className="text-[13px] font-bold text-muted">{groupName(group)}</div>
                  {entries.map((g) => {
                    const text = mode === 'event' ? { label: g.label, summary: g.summary } : giftText(family, g)
                    const on = key === g.key
                    return (
                      <button
                        key={g.key}
                        type="button"
                        role="radio"
                        aria-checked={on}
                        onClick={() => setKey(g.key)}
                        className={`flex cursor-pointer items-start gap-3 rounded-lg border-2 px-3 py-2 text-left ${on ? 'border-accent bg-accent-soft' : 'border-line bg-bg'}`}
                      >
                        <Icon name={on ? 'radio_button_checked' : 'radio_button_unchecked'} className="mt-0.5 text-[20px] text-accent" />
                        <span className="flex-1">
                          <span className="block font-semibold">{text.label}</span>
                          <span className="block text-sm text-muted">{text.summary}</span>
                        </span>
                      </button>
                    )
                  })}
                </div>
              ))}
              {groups.length === 0 && <p className="text-sm text-muted">{t('activities.gift.nothing')}</p>}
            </div>
            {chosenEntry?.when && (
              <Callout tone="sky" icon="info">
                {whenText(chosenEntry.when)}
              </Callout>
            )}
          </>
        )}

        {mode === 'file' && (
          <div className="flex flex-col gap-2">
            <p className="text-sm">{t('activities.gift.fileHelp', { types: extensions.map((e) => `.${e}`).join(', ') })}</p>
            <input
              type="file"
              accept={extensions.map((e) => `.${e}`).join(',')}
              onChange={(e) => openFile(e.target.files?.[0])}
              className="text-sm"
            />
            {file && <Badge tone="emerald" icon="description">{file.name}</Badge>}
          </div>
        )}
      </div>

      )}

      <div className="flex flex-col gap-3 rounded-xl border border-line bg-surface p-[22px]">
        <div className="text-lg font-bold">{t('activities.howTo', { game: gameOf(game).name })}</div>
        <ContentFile
          name={activity === 'raid' ? `activities/raid-${raid.kind}` : activity === 'circle' ? `activities/circle-${circle.kind}` : `activities/gift-${family}`}
        />
      </div>

      {errors.length > 0 && (
        <Callout tone="red" icon="error">
          <ul>
            {errors.map((e) => (
              <li key={e}>{t(`activities.errors.${e}`, { defaultValue: t(`failures.${e}`, { defaultValue: t('failures.unknown') }) })}</li>
            ))}
          </ul>
        </Callout>
      )}

      <div className="flex justify-end">
        <Button
          icon={activity === 'raid' ? 'swords' : activity === 'circle' ? 'group' : 'send'}
          disabled={
            !(activity === 'raid' ? raidReady(raid) : activity === 'circle' ? circleReady(circle) : chosen) || busy || !supported.includes(activity)
          }
          onClick={start}
        >
          {t(activity === 'raid' ? 'activities.raid.start' : activity === 'circle' ? 'activities.circle.start' : 'activities.send')}
        </Button>
      </div>
    </div>
  )
}

/** Mostra a atividade atual (rodando) ou a que acabou de terminar nesta visita. */
function showing(s: ActivitySnapshot) {
  return s.status === 'running' || (s.finishedAt !== null && Date.now() - new Date(s.finishedAt).getTime() < 10 * 60_000)
}

function Progress({ snapshot, onStop, onAgain }: { snapshot: ActivitySnapshot; onStop: () => void; onAgain: () => void }) {
  const { t } = useTranslation()
  const game = gameOf(snapshot.game)
  const kinds = new Set(snapshot.events.map((e) => e.kind))
  const circle = snapshot.kind === 'circle'
  const raid = snapshot.kind === 'raid' || circle
  const swsh = game.family === 'swsh' && !raid
  const running = snapshot.status === 'running'
  const battled = kinds.has('battle') || kinds.has('over')

  const steps = raid
    ? [
        { done: kinds.has('started'), label: t('activities.progress.started') },
        { done: kinds.has('lobby') || battled, label: t(circle ? 'activities.circle.progress.lobby' : 'activities.progress.lobby') },
        { done: battled, label: t(circle ? 'activities.circle.progress.started' : 'activities.progress.battle') },
        { done: kinds.has('over') || (!running && battled), label: t(circle ? 'activities.circle.progress.over' : 'activities.progress.over') },
      ]
    : swsh
    ? [
        { done: kinds.has('started'), label: t('activities.progress.started') },
        { done: kinds.has('advertising'), label: t('activities.progress.advertising') },
      ]
    : [
        { done: kinds.has('started'), label: t('activities.progress.started') },
        { done: kinds.has('waiting') || kinds.has('joined'), label: t('activities.progress.waiting') },
        { done: kinds.has('joined'), label: t('activities.progress.joined') },
        { done: kinds.has('delivered'), label: t('activities.progress.delivered') },
      ]

  return (
    <div className="flex flex-col gap-5">
      <PageTitle>{t('activities.title')}</PageTitle>
      <div className="flex flex-col gap-4 rounded-xl border border-line bg-surface p-[22px]">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-lg font-bold">
            {circle
              ? t(`activities.circle.kinds.${snapshot.raidKind}`)
              : raid
                ? t(`activities.raid.kinds.${snapshot.raidKind}`)
                : (snapshot.giftLabel ?? t('activities.gift.title'))}
          </span>
          {raid && snapshot.giftLabel && <Badge>{snapshot.giftLabel}</Badge>}
          <Badge>{versionName(snapshot.game)}</Badge>
        </div>
        <ol className="flex flex-col gap-2">
          {steps.map((s) => (
            <li key={s.label} className="flex items-center gap-2.5">
              <Icon name={s.done ? 'check_circle' : running ? 'pending' : 'radio_button_unchecked'} className={`text-[22px] ${s.done ? 'text-ok' : 'text-muted'}`} />
              <span className={s.done ? 'font-semibold' : 'text-muted'}>{s.label}</span>
            </li>
          ))}
        </ol>

        {running && swsh && kinds.has('advertising') && (
          <Callout tone="sky" icon="info">
            {t('activities.progress.swshConfirm')}
          </Callout>
        )}
        {running && raid && battled && (
          <Callout tone="sky" icon="info">
            {t(circle ? 'activities.circle.progress.running' : 'activities.progress.raidBattle')}
          </Callout>
        )}
        {snapshot.status === 'completed' && (
          <Callout tone="emerald" icon="check_circle">
            {circle ? t('activities.circle.result') : raid ? t('activities.result.raid') : swsh ? t('activities.result.swsh') : t('activities.result.frlg')}
          </Callout>
        )}
        {snapshot.status === 'cancelled' && <Callout tone="amber" icon="info">{t('activities.result.cancelled')}</Callout>}
        {snapshot.status === 'failed' && (
          <Callout tone="red" icon="error">
            {t(`activities.errors.${snapshot.errorCode}`, {
              defaultValue: t(`failures.${snapshot.errorCode}`, { defaultValue: t('failures.unknown') }),
            })}
          </Callout>
        )}

        <div className="flex justify-end gap-2">
          {running ? (
            swsh || (raid && battled) ? (
              <Button icon="check" onClick={onStop}>
                {t('activities.done')}
              </Button>
            ) : (
              <Button variant="secondary" icon="close" onClick={onStop}>
                {t('activities.stop')}
              </Button>
            )
          ) : (
            <Button icon="redeem" onClick={onAgain}>
              {t('activities.again')}
            </Button>
          )}
        </div>
      </div>
      {running && (
        <div className="flex flex-col gap-3 rounded-xl border border-line bg-surface p-[22px]">
          <div className="text-lg font-bold">{t('activities.howTo', { game: game.name })}</div>
          <ContentFile
            name={circle ? `activities/circle-${snapshot.raidKind}` : raid ? `activities/raid-${snapshot.raidKind}` : `activities/gift-${game.family}`}
          />
        </div>
      )}
    </div>
  )
}
