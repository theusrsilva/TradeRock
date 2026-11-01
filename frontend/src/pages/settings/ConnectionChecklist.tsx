import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ContentFile } from '../../components/Markdown'
import { SwitchReady } from '../../components/illustrations/SwitchReady'
import { Badge, Button, Callout, HelpTip, Icon, selectClass, useToast } from '../../components/ui'
import { api } from '../../lib/api'
import { downloadBlob } from '../../lib/appInfo'
import { useIsRemote } from '../../lib/auth'
import { connectionKeys, requiredKeys, systemCheckIds, useConnection, type ConnectionKey, type ItemState } from '../../lib/connection'
import type { HardwareCatalog, SetupStatus } from '../../lib/types'
import { Esp32Actions } from '../setup/Esp32Actions'
import { CatalogNotice, CheckList, CompatBadge, Esp32Table, WifiTable } from '../setup/parts'
import { SwitchTest } from '../setup/SwitchTest'
import { LanCard } from './LanCard'
import { WifiActions } from '../setup/WifiActions'

type Look = { chip: string; icon: string; label: string }

/** Cor, ícone e rótulo de cada situação (o rótulo é uma chave de tradução). */
export const stateLook: Record<ItemState | 'guide' | 'testing', Look> = {
  ok: { chip: 'bg-ok-soft text-ok', icon: 'check_circle', label: 'ui.conn.state.ok' },
  warn: { chip: 'bg-warn-soft text-warn', icon: 'warning', label: 'ui.conn.state.warn' },
  missing: { chip: 'bg-bad-soft text-bad', icon: 'cancel', label: 'ui.conn.state.missing' },
  blocked: { chip: 'bg-surface2 text-muted', icon: 'lock', label: 'ui.conn.state.blocked' },
  idle: { chip: 'bg-surface2 text-muted', icon: 'radio_button_unchecked', label: 'ui.conn.state.idle' },
  guide: { chip: 'bg-info-soft text-info', icon: 'menu_book', label: 'ui.conn.state.guide' },
  testing: { chip: 'bg-info-soft text-info', icon: 'progress_activity', label: 'ui.conn.state.testing' },
}

type Patch = Partial<Pick<SetupStatus['state'], 'radio' | 'phy' | 'serialPort' | 'esp32Board' | 'keysSkipped' | 'completed'>>
type RowKey = ConnectionKey | 'game'

/**
 * Configurações → Conexão: tudo o que a troca de verdade precisa, numa lista. Cada item mostra se
 * está OK, explica o que é (?) e abre o "como resolver" com as mesmas ações do antigo assistente.
 */
export function ConnectionChecklist() {
  const { t } = useTranslation()
  const remote = useIsRemote()
  const { setup, items, readyCount, ready, demo, reload, setSetup, reloadEngines } = useConnection()
  const [catalog, setCatalog] = useState<HardwareCatalog | null>(null)
  const [open, setOpen] = useState<RowKey | null>(null)
  const [checking, setChecking] = useState(false)
  const [activating, setActivating] = useState(false)
  const toast = useToast()
  const opened = useRef(false)

  useEffect(() => {
    if (remote) return
    void reload()
    api.get<HardwareCatalog>('/api/setup/catalog').then(setCatalog).catch(() => undefined)
  }, [remote, reload])

  // Abre sozinho o primeiro item que falta.
  useEffect(() => {
    if (!setup || opened.current) return
    opened.current = true
    setOpen(connectionKeys.find((k) => items[k] !== 'ok') ?? null)
  }, [setup, items])

  if (remote) return <RemoteStatus ready={ready} />
  if (!setup || !catalog) return <p className="text-muted">{t('wizard.detecting')}</p>

  const patch = async (p: Patch) => setSetup(await api.put<SetupStatus>('/api/setup/state', p))

  async function checkAgain() {
    setChecking(true)
    await reload()
    setChecking(false)
    toast(t('ui.conn.checked'))
  }

  async function exportReport() {
    const response = await fetch('/api/setup/report', { credentials: 'same-origin' })
    downloadBlob(await response.blob(), 'traderock-relatorio.md')
  }

  // Tudo pronto e ainda na simulação: liga o motor do rádio escolhido (o antigo "Concluir").
  async function activate() {
    if (!setup) return
    setActivating(true)
    try {
      await patch({ completed: true })
      if (setup.state.radio && !setup.system.simulatedScenario)
        await api.put('/api/engines/selected', { id: setup.state.radio === 'wifi' ? 'python-ldn' : 'esp32' })
      await reloadEngines()
      toast(t('ui.conn.activated'))
    } catch {
      toast(t('common.error'))
    } finally {
      setActivating(false)
    }
  }

  const { state, advice, system } = setup
  const wifi = state.radio === 'wifi'
  const esp32 = state.radio === 'esp32'
  const toggle = (k: RowKey) => setOpen((o) => (o === k ? null : k))
  const step4 = setup.checks.filter((c) => c.step === 4 && !systemCheckIds.includes(c.id))
  const firstProblem = (list: typeof setup.checks) => list.find((c) => c.status !== 'ok' && c.status !== 'info')
  const checkText = (c: (typeof setup.checks)[number] | undefined) => (c ? t(`wizard.checks.${c.code}`, c.args ?? {}) : '')

  const bestModel = advice.bestCard?.matches[0]?.model ?? advice.bestCard?.card.productName ?? advice.bestCard?.card.module ?? '?'
  const chosenCard = advice.cards.find((c) => c.card.phy === state.phy)
  const chosenModel = chosenCard ? (chosenCard.matches[0]?.model ?? chosenCard.card.productName ?? chosenCard.card.module ?? chosenCard.card.phy) : null
  const board = catalog.esp32.find((b) => b.id === state.esp32Board)
  const keysCheck = setup.checks.find((c) => c.id === 'keys')
  const switchCheck = setup.checks.find((c) => c.id === 'switch_test')

  const desc: Record<RowKey, string> = {
    system:
      items.system === 'ok'
        ? system.os === 'windows'
          ? t('ui.conn.desc.systemWindows')
          : t('ui.conn.desc.systemOk')
        : (checkText(firstProblem(setup.checks.filter((c) => systemCheckIds.includes(c.id)))) ||
          t(`ui.conn.desc.system_${advice.situation}`, { defaultValue: t('ui.conn.desc.systemWarn') })),
    keys:
      keysCheck?.code === 'keys_ok'
        ? t('ui.conn.desc.keysOk')
        : keysCheck?.code === 'keys_skipped'
          ? t('ui.conn.desc.keysSkipped')
          : keysCheck?.code === 'keys_invalid'
            ? t('wizard.keysInvalidTitle')
            : t('ui.conn.desc.keysMissing'),
    radio: !state.radio
      ? t('ui.conn.desc.radioNone')
      : wifi
        ? chosenModel
          ? t('ui.conn.desc.radioWifiCard', { model: chosenModel })
          : t('ui.conn.desc.radioWifi')
        : board
          ? t('ui.conn.desc.radioEsp32Board', { model: board.model })
          : t('ui.conn.desc.radioEsp32'),
    driver: !state.radio
      ? t('ui.conn.desc.driverNoRadio')
      : items.driver === 'ok'
        ? wifi
          ? t('ui.conn.desc.driverWifiOk')
          : t('ui.conn.desc.driverEspOk')
        : checkText(firstProblem(step4)),
    game: t('ui.conn.desc.game'),
    switch:
      items.switch === 'ok'
        ? t('ui.conn.desc.switchOk')
        : items.switch === 'idle'
          ? t('ui.conn.desc.switchIdle', { seconds: setup.switchTestSeconds })
          : checkText(switchCheck),
  }

  const rows: { key: RowKey; title: string; state: ItemState | 'guide'; action?: ReactNode; body: ReactNode }[] = [
    {
      key: 'system',
      title: t('ui.conn.title.system'),
      state: items.system,
      action: (
        <Button variant="secondary" disabled={checking} onClick={() => void checkAgain()}>
          {t('ui.conn.test')}
        </Button>
      ),
      body: (
        <>
          <Callout tone={advice.situation === 'wifi_compatible' ? 'emerald' : advice.situation === 'wifi_test_first' ? 'amber' : 'sky'}>
            {t(`wizard.preview.${advice.situation}`, {
              model: bestModel,
              vm: system.virtualMachine ?? '',
              why: t(`wizard.why.${advice.bestCard?.status === 'unstable' ? 'unstable' : 'untested'}`),
            })}
          </Callout>
          <CheckList checks={setup.checks.filter((c) => systemCheckIds.includes(c.id))} />
          <FullText names={['wizard/01-boas-vindas', 'wizard/01-precisa-todos', system.os === 'windows' ? 'wizard/01-precisa-windows' : 'wizard/01-precisa-linux']} />
        </>
      ),
    },
    {
      key: 'keys',
      title: t('ui.conn.title.keys'),
      state: items.keys,
      action:
        keysCheck?.code === 'keys_ok' ? undefined : (
          <Button onClick={() => setOpen('keys')}>{t('wizard.keysUpload')}</Button>
        ),
      body: <KeysPanel status={setup} onChange={() => void reload()} onSkip={() => void patch({ keysSkipped: true })} />,
    },
    {
      key: 'radio',
      title: t('ui.conn.title.radio'),
      state: items.radio,
      body: <RadioPanel status={setup} catalog={catalog} onPatch={(p) => void patch(p)} onRefresh={() => void checkAgain()} />,
    },
    {
      key: 'driver',
      title: esp32 ? t('ui.conn.title.driverEsp32') : t('ui.conn.title.driver'),
      state: items.driver,
      body: !state.radio ? (
        <p className="text-sm text-muted">{t('ui.conn.desc.driverNoRadio')}</p>
      ) : (
        <>
          <CheckList checks={step4} />
          {wifi ? (
            <WifiActions status={setup} onStatus={setSetup} onUseEsp32={() => void patch({ radio: 'esp32' })} />
          ) : (
            <Esp32Actions status={setup} onStatus={setSetup} />
          )}
          <Button variant="secondary" icon="refresh" onClick={() => void checkAgain()} className="self-start">
            {t('wizard.refresh')}
          </Button>
          <FullText names={[wifi ? 'wizard/04-placa' : 'wizard/04-esp32']} />
        </>
      ),
    },
    {
      key: 'game',
      title: t('ui.conn.title.game'),
      state: 'guide',
      body: (
        <>
          <SwitchReady labels={[t('trade.reminder.illustration.center'), t('trade.reminder.illustration.counter'), t('trade.reminder.illustration.leader')]} />
          <Steps items={(t('ui.trade.prep', { returnObjects: true }) as { title: string; sub: string }[]).map((p) => `${p.title}: ${p.sub}`)} />
          <FullText names={['wizard/05-jogo']} />
        </>
      ),
    },
    {
      key: 'switch',
      title: t('ui.conn.title.switch'),
      state: items.switch,
      action:
        items.switch === 'blocked' ? undefined : (
          <Button variant={items.switch === 'ok' ? 'secondary' : 'primary'} onClick={() => setOpen('switch')}>
            {items.switch === 'ok' ? t('wizard.switchTest.again') : t('ui.conn.testNow')}
          </Button>
        ),
      body: (
        <>
          {state.radio && <SwitchTest status={setup} onStatus={setSetup} />}
          <CheckList checks={setup.checks.filter((c) => c.step === 6)} />
          <FullText names={['wizard/06-teste']} />
        </>
      ),
    },
  ]

  const allOk = setup.readyToTrade
  const ringColor = allOk ? 'var(--tr-ok)' : 'var(--tr-warn)'

  return (
    <div className="flex flex-col gap-3.5">
      <div className="flex flex-wrap items-center gap-4 rounded-xl border border-line bg-surface px-5 py-[18px]">
        <div
          className="flex h-[52px] w-[52px] shrink-0 items-center justify-center rounded-full"
          style={{ background: `conic-gradient(${ringColor} ${(readyCount / requiredKeys.length) * 100}%, var(--tr-surface2) 0)` }}
        >
          <div className="flex h-10 w-10 items-center justify-center rounded-full bg-surface text-sm font-extrabold">{readyCount}/{requiredKeys.length}</div>
        </div>
        <div className="min-w-[200px] flex-1">
          <div className="text-[17px] font-extrabold">{allOk ? t('ui.home.allReady') : t('ui.home.missing', { count: requiredKeys.length - readyCount })}</div>
          <div className="text-sm text-muted">{allOk && demo ? t('ui.conn.activateHelp') : t('ui.conn.summarySub')}</div>
        </div>
        {allOk && demo && (
          <Button icon="bolt" disabled={activating} onClick={() => void activate()}>
            {t('ui.conn.activate')}
          </Button>
        )}
        <Button variant={allOk && demo ? 'secondary' : 'primary'} icon={checking ? 'progress_activity' : 'play_arrow'} disabled={checking} onClick={() => void checkAgain()}>
          {checking ? t('ui.conn.checking') : t('ui.conn.checkAll')}
        </Button>
        <Button variant="secondary" icon="description" title={t('ui.conn.reportHint')} onClick={() => void exportReport()}>
          {t('wizard.exportReport')}
        </Button>
      </div>

      <LanCard />

      {rows.map((row) => {
        const look = stateLook[checking && row.state !== 'guide' ? 'testing' : row.state]
        const isOpen = open === row.key
        return (
          <div key={row.key} className={`rounded-xl border bg-surface ${isOpen ? 'border-accent' : 'border-line'}`}>
            <div className="flex flex-wrap items-center gap-3 px-[18px] py-4">
              <span className={`flex h-[38px] w-[38px] shrink-0 items-center justify-center rounded-full ${look.chip}`}>
                <Icon name={look.icon} className="text-[22px]" />
              </span>
              <div className="flex min-w-0 flex-[1_1_220px] flex-col">
                <div className="flex items-center gap-2">
                  <button type="button" onClick={() => toggle(row.key)} className="cursor-pointer text-left text-[15.5px] font-bold">
                    {row.title}
                  </button>
                  <HelpTip>{t(`ui.conn.what.${row.key === 'driver' && esp32 ? 'usb' : row.key}`)}</HelpTip>
                </div>
                <button type="button" onClick={() => toggle(row.key)} className="cursor-pointer text-left text-[13.5px] text-muted [overflow-wrap:anywhere]">
                  {desc[row.key]}
                </button>
              </div>
              <span className={`flex h-[26px] items-center rounded-full px-2.5 text-[12.5px] font-bold whitespace-nowrap ${look.chip}`}>{t(look.label)}</span>
              {row.action}
              <button
                type="button"
                onClick={() => toggle(row.key)}
                aria-expanded={isOpen}
                aria-label={t('wizard.howToFix')}
                title={t('wizard.howToFix')}
                className="flex h-[34px] w-[34px] cursor-pointer items-center justify-center rounded-full text-muted"
              >
                <Icon name={isOpen ? 'expand_less' : 'expand_more'} className="text-[22px]" />
              </button>
            </div>
            {isOpen && <div className="flex flex-col gap-3 px-[18px] pb-[18px]">{row.body}</div>}
          </div>
        )
      })}
    </div>
  )
}

/** Texto explicativo completo do antigo assistente, recolhido para a lista ficar curta. */
function FullText({ names }: { names: string[] }) {
  const { t } = useTranslation()
  return (
    <details className="rounded-lg border border-line px-3 py-2 text-sm">
      <summary className="cursor-pointer font-semibold text-accent">{t('ui.conn.fullText')}</summary>
      <div className="mt-2 flex flex-col gap-3">
        {names.map((n) => (
          <ContentFile key={n} name={n} />
        ))}
      </div>
    </details>
  )
}

/** Passos curtos de "como resolver". */
function Steps({ items }: { items: string[] }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-2">
      <div className="text-xs font-bold tracking-[.7px] text-muted uppercase">{t('wizard.howToFix')}</div>
      {items.map((text, i) => (
        <div key={text} className="flex items-start gap-2.5 text-sm">
          <span className="flex h-[22px] w-[22px] shrink-0 items-center justify-center rounded-full bg-surface2 text-xs font-bold">{i + 1}</span>
          <span>{text}</span>
        </div>
      ))}
    </div>
  )
}

/** Pela rede: a conexão é do PC do dono; aqui só o status. */
function RemoteStatus({ ready }: { ready: boolean }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-3.5">
      <Callout tone="sky" icon="computer">
        {t('ui.conn.remote')}
      </Callout>
      <div className="flex items-center gap-4 rounded-xl border border-line bg-surface px-5 py-[18px]">
        <span className={`flex h-12 w-12 items-center justify-center rounded-full ${ready ? 'bg-ok-soft text-ok' : 'bg-warn-soft text-warn'}`}>
          <Icon name={ready ? 'check_circle' : 'science'} className="text-[26px]" />
        </span>
        <div>
          <div className="text-[17px] font-extrabold">{ready ? t('ui.status.ready') : t('ui.status.demo')}</div>
          <div className="text-sm text-muted">{ready ? t('ui.home.readySub') : t('demo.explain')}</div>
        </div>
      </div>
    </div>
  )
}

function KeysPanel({ status, onChange, onSkip }: { status: SetupStatus; onChange: () => void; onSkip: () => void }) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const [problem, setProblem] = useState<SetupStatus['keys'] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const keys = status.keys

  async function upload(file: File) {
    setProblem(null)
    setError(null)
    const form = new FormData()
    form.append('file', file, 'prod.keys')
    // Um envio só. Em caso de formato inválido, a resposta traz a validação (nomes das chaves, nunca valores).
    const response = await fetch('/api/setup/keys', { method: 'POST', body: form, credentials: 'same-origin' })
    if (response.ok) {
      onChange()
      return
    }
    try {
      const body = (await response.json()) as { error?: string; validation?: SetupStatus['keys'] }
      if (body.validation) setProblem(body.validation)
      else setError(body.error ?? 'error')
    } catch {
      setError('error')
    }
  }

  const shown = problem ?? (keys && !keys.valid ? keys : null)

  return (
    <>
      <Steps items={t('ui.conn.keysSteps', { returnObjects: true }) as string[]} />
      <input ref={input} type="file" hidden onChange={(e) => { if (e.target.files?.[0]) void upload(e.target.files[0]); e.target.value = '' }} />
      {keys?.valid && <Callout tone="emerald" icon="check_circle">{t('wizard.keysOk', { protocols: keys.protocols.join(', ') })}</Callout>}
      {shown && (
        <Callout tone="red" icon="error">
          <p className="font-bold">{t('wizard.keysInvalidTitle')}</p>
          {shown.missingRequired.length > 0 && <p>• {t('wizard.keysMissing', { keys: shown.missingRequired.join(', ') })}</p>}
          {shown.invalidLines.length > 0 && <p>• {t('wizard.keysLines', { lines: shown.invalidLines.join(', ') })}</p>}
        </Callout>
      )}
      {error && <p className="text-sm text-bad">{t('common.error')}</p>}
      <Callout tone="amber" icon="shield">
        {t('ui.conn.keysNote')}
      </Callout>
      <div className="flex flex-wrap gap-2">
        <Button icon="upload_file" onClick={() => input.current?.click()}>
          {keys ? t('wizard.keysReplace') : t('wizard.keysUpload')}
        </Button>
        {keys && (
          <Button variant="ghost" onClick={() => void api.delete('/api/setup/keys').then(onChange)}>
            {t('wizard.keysRemove')}
          </Button>
        )}
        {!keys?.valid && (
          <Button variant="secondary" onClick={onSkip}>
            {t('wizard.keysSkip')}
          </Button>
        )}
      </div>
      <FullText names={['wizard/02-prod-keys']} />
    </>
  )
}

function RadioPanel({
  status,
  catalog,
  onPatch,
  onRefresh,
}: {
  status: SetupStatus
  catalog: HardwareCatalog
  onPatch: (p: Patch) => void
  onRefresh: () => void
}) {
  const { t } = useTranslation()
  const [showKnown, setShowKnown] = useState(false)
  const { advice, state, system } = status
  const windows = system.os === 'windows'
  const detectedDrivers = advice.cards.flatMap((c) => [c.card.module, c.card.driver]).filter((d): d is string => !!d)
  // No Linux, a placa Wi-Fi aparece primeiro; no Windows, só o ESP32.
  const options = windows ? advice.options.filter((o) => o.engine === 'esp32') : advice.options
  const ports = system.serialPorts.filter((p) => !p.driverMissing)

  return (
    <>
      <div className="grid gap-2.5 [grid-template-columns:repeat(auto-fit,minmax(220px,1fr))]">
        {options.map((o) => {
          const chosen = state.radio === o.engine
          return (
            <button
              key={o.engine}
              type="button"
              disabled={!o.available}
              onClick={() => onPatch({ radio: o.engine })}
              aria-pressed={chosen}
              className={`flex cursor-pointer flex-col gap-1.5 rounded-lg border-2 p-3.5 text-left disabled:cursor-not-allowed disabled:opacity-55 ${chosen ? 'border-accent bg-accent-soft' : 'border-line bg-bg'}`}
            >
              <span className="flex flex-wrap items-center gap-2">
                <Icon name={o.engine === 'wifi' ? 'wifi' : 'memory'} className="text-[22px] text-accent" />
                <b className="flex-1">{t(`ui.conn.radio.${o.engine}`)}</b>
                {o.recommended && <Badge tone="emerald">{t('wizard.recommended')}</Badge>}
                {o.engine === 'wifi' && advice.situation === 'wifi_compatible' && <Badge tone="emerald">{t('wizard.noBuy')}</Badge>}
                {!o.available && <Badge>{t('wizard.unavailable')}</Badge>}
              </span>
              <span className="text-[13px] text-muted">{t(`ui.conn.radio.${o.engine}Sub`)}</span>
              {o.reasonCode && <span className="text-[13px] text-muted">{t(`wizard.reasons.${o.reasonCode}`)}</span>}
              {chosen && (
                <span className="flex items-center gap-1 text-[13px] font-bold text-accent">
                  <Icon name="check_circle" className="text-[17px]" />
                  {t('wizard.chosen')}
                </span>
              )}
            </button>
          )
        })}
      </div>

      {advice.suggestedUsbAdapters.length > 0 && !windows && (
        <div className="rounded-lg bg-surface2 p-3 text-sm">
          <p className="font-bold">{t('wizard.usbAdapterTitle')}</p>
          <p>{advice.suggestedUsbAdapters.map((a) => a.model).join(', ')}</p>
          <p className="mt-1 text-xs text-muted">{t('wizard.usbAdapterPros')}</p>
          <p className="text-xs text-muted">{t('wizard.esp32Pros')}</p>
        </div>
      )}

      {state.radio === 'wifi' && (
        <div className="flex flex-col gap-1.5">
          <div className="text-[13px] font-bold">{t('ui.conn.cardsFound')}</div>
          {advice.cards.length === 0 && <p className="text-sm text-muted">{t('wizard.checks.no_wifi_card')}</p>}
          {advice.cards.map((c) => {
            const on = state.phy === c.card.phy
            return (
              <button
                key={c.card.phy}
                type="button"
                onClick={() => onPatch({ phy: c.card.phy })}
                aria-pressed={on}
                className={`flex min-h-12 cursor-pointer flex-wrap items-center gap-2.5 rounded-lg border-[1.5px] bg-bg px-3 py-2 text-left ${on ? 'border-accent' : 'border-line'}`}
              >
                <Icon name={on ? 'radio_button_checked' : 'radio_button_unchecked'} className={on ? 'text-accent' : 'text-muted'} />
                <span className="min-w-0 flex-1 text-sm font-semibold">
                  {c.matches[0]?.model ?? c.card.productName ?? c.card.module}{' '}
                  <span className="text-[12.5px] font-normal text-muted">
                    · {c.card.bus.toUpperCase()} · <span className="font-mono">{c.card.phy}</span> · <span className="font-mono">{c.card.module ?? c.card.driver}</span>
                  </span>
                </span>
                <CompatBadge status={c.status} />
              </button>
            )
          })}
        </div>
      )}

      {state.radio === 'esp32' && (
        <div className="flex flex-col gap-3">
          <details className="rounded-lg border border-line px-3 py-2">
            <summary className="cursor-pointer text-sm font-semibold text-accent">{t('ui.conn.esp32Guide')}</summary>
            <div className="mt-2 flex flex-col gap-3">
              <ContentFile name="wizard/03-guia-esp32" />
            </div>
          </details>
          <label className="flex flex-col gap-1.5 text-sm">
            <span className="font-bold">{t('wizard.chooseBoard')}</span>
            <select className={selectClass} value={state.esp32Board ?? ''} onChange={(e) => onPatch({ esp32Board: e.target.value })}>
              <option value="" disabled>
                {t('wizard.chooseBoardPlaceholder')}
              </option>
              {catalog.esp32.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.model} — {t(`wizard.compat.${b.status}`)}
                </option>
              ))}
            </select>
          </label>
          <div className="flex flex-col gap-1.5">
            <div className="text-[13px] font-bold">{t('wizard.choosePort')}</div>
            {ports.length === 0 && <p className="text-sm text-muted">{t('wizard.noPorts')}</p>}
            {ports.map((p) => {
              const chip = catalog.usbSerial.find((u) => u.vid === p.vendorId && u.pid === p.productId)
              const on = state.serialPort === p.path
              return (
                <button
                  key={p.path}
                  type="button"
                  onClick={() => onPatch({ serialPort: p.path })}
                  aria-pressed={on}
                  className={`flex min-h-12 cursor-pointer items-center gap-2.5 rounded-lg border-[1.5px] bg-bg px-3 py-2 text-left ${on ? 'border-accent' : 'border-line'}`}
                >
                  <Icon name={on ? 'radio_button_checked' : 'radio_button_unchecked'} className={on ? 'text-accent' : 'text-muted'} />
                  <span className="font-mono text-sm">{p.path}</span>
                  <span className="text-[13px] text-muted">· {chip?.name ?? p.productName ?? `${p.vendorId}:${p.productId}`}</span>
                </button>
              )
            })}
          </div>
        </div>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <button type="button" onClick={() => setShowKnown(!showKnown)} className="flex cursor-pointer items-center gap-1 py-1 text-[13.5px] font-semibold text-accent">
          <Icon name={showKnown ? 'expand_less' : 'expand_more'} className="text-[18px]" />
          {state.radio === 'esp32' || windows ? t('wizard.catalogEsp32') : t('wizard.catalogWifi')}
        </button>
        <Button variant="ghost" size="sm" icon="refresh" onClick={onRefresh}>
          {t('wizard.refresh')}
        </Button>
      </div>
      <FullText names={[windows ? 'wizard/03-radio-windows' : 'wizard/03-radio-linux']} />
      {showKnown && (
        <div className="flex flex-col gap-2 rounded-lg border border-line p-3">
          <CatalogNotice catalog={catalog} />
          {state.radio === 'esp32' || windows ? (
            <Esp32Table entries={catalog.esp32} highlight={state.esp32Board} />
          ) : (
            <WifiTable entries={catalog.wifi} highlight={detectedDrivers} />
          )}
        </div>
      )}
    </>
  )
}
