import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '../../components/ui'
import { api, ApiError } from '../../lib/api'
import type { CardTestResult, SetupStatus } from '../../lib/types'
import { AppImageWorkerInstall } from './AppImageWorker'

/**
 * Etapa 4 com a placa Wi-Fi: teste real da placa e configuração do NetworkManager.
 * As duas ações rodam no componente de rede (worker); na primeira vez o sistema pede a senha de
 * administrador, num diálogo do próprio sistema (o TradeRock nunca vê a senha).
 */
export function WifiActions({
  status,
  onStatus,
  onUseEsp32,
}: {
  status: SetupStatus
  onStatus: (s: SetupStatus) => void
  onUseEsp32: () => void
}) {
  const { t } = useTranslation()
  const { state, system, checks, advice } = status
  const [busy, setBusy] = useState<'test' | 'network' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirmNetwork, setConfirmNetwork] = useState(false)

  const card = advice.cards.find((c) => c.card.phy === state.phy)
  const workerOk = checks.find((c) => c.id === 'worker')?.status === 'ok'
  const toolsOk = !checks.some((c) => c.id === 'tools')
  const cardDriver = card ? (card.card.module ?? card.card.driver) : null
  // O resultado vale para esta placa: mesmo phy e mesmo driver (phyN pode virar outra placa).
  const test = state.cardTest && state.cardTest.phy === state.phy && (!state.cardTest.driver || state.cardTest.driver === cardDriver) ? state.cardTest : null
  const canTest = workerOk && toolsOk && busy === null

  async function run(kind: 'test' | 'network', action: () => Promise<void>) {
    setBusy(kind)
    setError(null)
    try {
      await action()
    } catch (e) {
      setError(e instanceof ApiError && e.code ? e.code : 'unknown')
    } finally {
      setBusy(null)
    }
  }

  const runTest = () =>
    run('test', async () => {
      const res = await api.post<{ result: CardTestResult; status: SetupStatus }>('/api/setup/card-test', { phy: state.phy })
      onStatus(res.status)
    })

  const installNetwork = () =>
    run('network', async () => {
      onStatus(await api.post<SetupStatus>('/api/setup/network-config'))
      setConfirmNetwork(false)
    })

  if (!card) return null

  return (
    <div className="space-y-3 rounded-xl border border-slate-200 p-4 dark:border-slate-800">
      <h3 className="font-semibold">{t('wizard.cardTest.title', { phy: card.card.phy })}</h3>
      <p className="text-sm">{t('wizard.cardTest.help')}</p>
      <ul className="list-disc space-y-1 pl-5 text-sm text-slate-600 dark:text-slate-300">
        <li>{t('wizard.cardTest.password')}</li>
        <li>{card.card.isInternetConnection ? t('wizard.cardTest.offlineInternet') : t('wizard.cardTest.offline')}</li>
      </ul>

      {!workerOk && <p className="text-sm text-amber-700 dark:text-amber-300">{t('wizard.cardTest.needsWorker')}</p>}
      <AppImageWorkerInstall status={status} onStatus={onStatus} />
      {workerOk && !toolsOk && <p className="text-sm text-amber-700 dark:text-amber-300">{t('wizard.cardTest.needsTools')}</p>}

      <div className="flex flex-wrap items-center gap-2">
        <Button onClick={() => void runTest()} disabled={!canTest}>
          {busy === 'test' ? t('wizard.cardTest.running') : test ? t('wizard.cardTest.again') : t('wizard.cardTest.run')}
        </Button>
        {busy === 'test' && <span className="text-sm text-slate-500">{t('wizard.cardTest.waitPassword')}</span>}
      </div>

      {error && (
        <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-200">
          {t(`failures.${error}`, { defaultValue: t('failures.unknown') })}
        </p>
      )}

      {test && <CardTestView test={test} />}

      {test && !test.passed && (
        <div className="space-y-2 rounded-lg bg-slate-50 p-3 text-sm dark:bg-slate-900">
          <p className="font-medium">{t('wizard.cardTest.whatNow')}</p>
          <p>{advice.cards.length > 1 ? t('wizard.cardTest.optionsMany') : t('wizard.cardTest.optionsOne')}</p>
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => void runTest()} disabled={!canTest}>
              {t('wizard.cardTest.again')}
            </Button>
            <Button variant="secondary" onClick={onUseEsp32}>
              {t('wizard.cardTest.useEsp32')}
            </Button>
          </div>
        </div>
      )}

      {system.networkManagerRunning && !system.ldnNetwork?.complete && (
        <div className="space-y-2 border-t border-slate-200 pt-3 text-sm dark:border-slate-800">
          <h4 className="font-semibold">{t('wizard.network.title')}</h4>
          <p>{t('wizard.network.help')}</p>
          {!confirmNetwork ? (
            <Button variant="secondary" onClick={() => setConfirmNetwork(true)} disabled={!workerOk || busy !== null}>
              {t('wizard.network.configure')}
            </Button>
          ) : (
            <div className="space-y-2 rounded-lg bg-amber-50 p-3 dark:bg-amber-950/40">
              <p>{t('wizard.network.confirmText')}</p>
              <ul className="list-disc pl-5 font-mono text-xs">
                <li>/etc/NetworkManager/conf.d/zz-ldn-unmanaged.conf</li>
                <li>/etc/systemd/network/10-traderock-ldn.link</li>
              </ul>
              <div className="flex gap-2">
                <Button onClick={() => void installNetwork()} disabled={busy !== null}>
                  {busy === 'network' ? t('wizard.cardTest.running') : t('wizard.network.confirm')}
                </Button>
                <Button variant="ghost" onClick={() => setConfirmNetwork(false)}>
                  {t('common.cancel')}
                </Button>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}

function CardTestView({ test }: { test: CardTestResult }) {
  const { t, i18n } = useTranslation()
  return (
    <div
      className={`space-y-2 rounded-lg p-3 text-sm ${
        test.passed ? 'bg-emerald-50 text-emerald-900 dark:bg-emerald-950/40 dark:text-emerald-100' : 'bg-red-50 text-red-900 dark:bg-red-950/40 dark:text-red-100'
      }`}
    >
      <p className="font-medium">
        {t(`wizard.cardTest.codes.${test.code}`, { defaultValue: test.code })}
        {test.simulated && ` · ${t('wizard.cardTest.simulated')}`}
      </p>
      {test.channels.length > 0 && (
        <div className="overflow-x-auto">
          <table className="text-xs">
            <thead>
              <tr className="text-left">
                <th className="pr-4">{t('wizard.cardTest.channel')}</th>
                <th className="pr-4">{t('wizard.cardTest.sent')}</th>
                <th className="pr-4">{t('wizard.cardTest.received')}</th>
              </tr>
            </thead>
            <tbody>
              {test.channels.map((c) => (
                <tr key={c.channel}>
                  <td className="pr-4">{c.channel}</td>
                  <td className="pr-4">{c.set ? c.sent : '—'}</td>
                  <td className="pr-4">{c.set ? c.received : t('wizard.cardTest.channelFailed')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {test.error && <p className="font-mono text-xs opacity-80">{test.error}</p>}
      <p className="text-xs opacity-70">{new Date(test.at).toLocaleString(i18n.language)}</p>
    </div>
  )
}
