import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Modal, inputClass } from '../../components/ui'
import { ApiError, api, spriteUrl } from '../../lib/api'
import { downloadBlob, useAppInfo } from '../../lib/appInfo'
import type { SaveLayoutInfo, VaultPokemon } from '../../lib/types'

/** Injetar Pokémon selecionados do cofre nas posições livres de uma caixa de um save de GBA. */
export function InjectDialog({ pokemon, onClose }: { pokemon: VaultPokemon[]; onClose: () => void }) {
  const { t } = useTranslation()
  const info = useAppInfo()
  const input = useRef<HTMLInputElement>(null)
  const [save, setSave] = useState<SaveLayoutInfo | null>(null)
  const [box, setBox] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<{ backup: string; written: string | null; download: boolean; count: number } | null>(null)

  const errorText = (e: unknown) => {
    const code = e instanceof ApiError ? e.code : null
    return code ? t(`inject.errors.${code}`, { defaultValue: t('common.error') }) : t('common.error')
  }

  async function openNative() {
    setError(null)
    setBusy(true)
    try {
      const r = await api.post<SaveLayoutInfo | undefined>('/api/saves/open')
      if (r) setSave(r)
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }

  async function upload(file: File) {
    setError(null)
    setBusy(true)
    const form = new FormData()
    form.append('file', file, file.name)
    try {
      setSave(await api.post<SaveLayoutInfo>('/api/saves/upload', form))
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }

  const occupied = new Set(save?.layout.occupied.filter((o) => o.box === box).map((o) => o.slot) ?? [])
  const freeSlots = save ? Array.from({ length: save.layout.slotsPerBox }, (_, i) => i).filter((i) => !occupied.has(i)) : []
  const placements = pokemon.slice(0, freeSlots.length).map((p, i) => ({ pokemonId: p.id, box, slot: freeSlots[i] }))
  const plannedSlot = new Map(placements.map((p) => [p.slot, pokemon.find((x) => x.id === p.pokemonId)!]))

  async function inject() {
    if (!save) return
    setBusy(true)
    setError(null)
    try {
      const r = await api.post<{ backup: string; written: string | null; download: boolean; save: SaveLayoutInfo }>(
        `/api/saves/${save.token}/inject`,
        { placements },
      )
      setResult({ backup: r.backup, written: r.written, download: r.download, count: placements.length })
      setSave(r.save)
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }

  async function download() {
    if (!save) return
    const response = await fetch(`/api/saves/${save.token}/download`, { credentials: 'same-origin' })
    downloadBlob(await response.blob(), save.fileName)
  }

  return (
    <Modal
      title={t('inject.title')}
      actions={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          {save && !result && (
            <Button disabled={busy || pokemon.length === 0 || pokemon.length > freeSlots.length} onClick={() => void inject()}>
              {t('inject.confirm', { count: placements.length })}
            </Button>
          )}
        </>
      }
    >
      <div className="space-y-3">
        <p>{t('inject.intro')}</p>
        {pokemon.length === 0 && <p className="text-warn">{t('inject.noneSelected')}</p>}

        {!save && (
          <>
            <input ref={input} type="file" accept=".sav,.srm,.fla,.dsv" hidden onChange={(e) => e.target.files?.[0] && void upload(e.target.files[0])} />
            <Button disabled={busy || pokemon.length === 0} onClick={() => (info?.window ? void openNative() : input.current?.click())}>
              {busy ? t('inject.reading') : t('inject.chooseFile')}
            </Button>
          </>
        )}

        {save && (
          <>
            <p className="font-medium">{t('inject.header', { file: save.fileName, game: save.layout.game, trainer: save.layout.trainerName })}</p>
            <p className="text-xs">{save.inPlace ? t('inject.inPlace') : t('inject.downloadMode')}</p>
            <p className="text-xs text-warn">⚠ {t('inject.emulatorWarning')}</p>
            <label className="block">
              <span className="text-xs font-medium">{t('inject.box')}</span>
              <select className={inputClass} value={box} disabled={!!result} onChange={(e) => setBox(Number(e.target.value))}>
                {save.layout.boxNames.map((name, i) => (
                  <option key={i} value={i}>
                    {i + 1}. {name}
                  </option>
                ))}
              </select>
            </label>
            <p className="text-xs">
              {t('inject.free', { count: freeSlots.length })} · {t('inject.pokemonToInject', { count: pokemon.length })} · {t('inject.autoPlace')}
            </p>
            {pokemon.length > freeSlots.length && <p className="text-bad">{t('inject.notEnough')}</p>}
            <div className="grid grid-cols-6 gap-1">
              {Array.from({ length: save.layout.slotsPerBox }, (_, slot) => {
                const there = save.layout.occupied.find((o) => o.box === box && o.slot === slot)
                const planned = !result ? plannedSlot.get(slot) : undefined
                const species = there?.species ?? planned?.species
                return (
                  <div
                    key={slot}
                    title={there ? `${there.nickname} (${there.speciesName})` : planned ? planned.nickname || planned.speciesName : ''}
                    className={`flex aspect-square items-center justify-center rounded border text-[10px] ${
                      planned ? 'border-accent bg-accent-soft' : there ? 'border-line bg-surface2' : 'border-dashed border-line'
                    }`}
                  >
                    {species ? <img src={spriteUrl(species)} alt="" className={`h-8 w-8 [image-rendering:pixelated] ${there ? 'opacity-60' : ''}`} /> : slot + 1}
                  </div>
                )
              })}
            </div>
          </>
        )}

        {error && <p className="text-bad">{error}</p>}
        {result && (
          <div className="space-y-1 rounded-lg bg-ok-soft p-3 text-ok">
            <p className="font-medium">{t('inject.done', { count: result.count })}</p>
            <p className="break-all text-xs">{t('inject.backup', { path: result.backup })}</p>
            {result.written && <p className="break-all text-xs">{t('inject.written', { path: result.written })}</p>}
            {result.download && (
              <Button variant="secondary" onClick={() => void download()}>
                {t('inject.download')}
              </Button>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
