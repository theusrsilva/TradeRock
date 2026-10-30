import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Badge, Button, Icon, useToast } from '../../components/ui'
import { ApiError, api, spriteUrl } from '../../lib/api'
import { useConnection } from '../../lib/connection'

/**
 * "Rede de casa" (D24): o Sword/Shield troca pela rede de casa no modo LAN do jogo, sem ESP32, placa Wi-Fi nem
 * prod.keys. É um motor a mais ("lan"), escolhido aqui; os outros jogos continuam pela conexão de rádio.
 */
export function LanCard() {
  const { t } = useTranslation()
  const toast = useToast()
  const { engines, setup, reloadEngines } = useConnection()
  const [busy, setBusy] = useState(false)
  const lan = engines.find((e) => e.id === 'lan')
  if (!lan) return null
  const radioEngine = setup?.state.radio === 'wifi' ? 'python-ldn' : setup?.state.radio === 'esp32' ? 'esp32' : null
  const back = engines.find((e) => e.id === radioEngine && e.available) ?? engines.find((e) => e.isSimulation)

  async function select(id: string) {
    setBusy(true)
    try {
      await api.put('/api/engines/selected', { id })
      await reloadEngines()
      toast(t('common.saved'))
    } catch (e) {
      toast(e instanceof ApiError ? t(`failures.${e.code ?? 'unknown'}`, { defaultValue: t('common.error') }) : t('common.error'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex flex-wrap items-start gap-4 rounded-xl border border-line bg-surface px-5 py-[18px]">
      <img src={spriteUrl(888)} alt="" className="pixelated h-12 w-12" />
      <div className="flex min-w-[220px] flex-1 flex-col gap-1">
        <div className="flex items-center gap-2 text-[17px] font-extrabold">
          {t('ui.conn.lan.title')}
          {lan.selected && <Badge tone="emerald">{t('ui.conn.lan.inUse')}</Badge>}
        </div>
        <div className="text-sm text-muted">{t('ui.conn.lan.sub')}</div>
        <ol className="mt-1 list-decimal space-y-0.5 pl-5 text-sm">
          <li>{t('ui.conn.lan.step1')}</li>
          <li>{t('ui.conn.lan.step2')}</li>
        </ol>
        {!lan.available && (
          <div className="flex items-center gap-1.5 text-[13px] text-warn">
            <Icon name="warning" className="text-base" />
            {t(`failures.${lan.reasonCode ?? 'unknown'}`, { defaultValue: t('common.error') })}
          </div>
        )}
      </div>
      {lan.selected ? (
        back && (
          <Button variant="secondary" icon="undo" disabled={busy} onClick={() => void select(back.id)}>
            {back.isSimulation ? t('ui.conn.lan.backDemo') : t('ui.conn.lan.backRadio')}
          </Button>
        )
      ) : (
        <Button icon="lan" disabled={busy || !lan.available} onClick={() => void select('lan')}>
          {t('ui.conn.lan.use')}
        </Button>
      )}
    </div>
  )
}
