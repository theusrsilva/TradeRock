import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { LegalityBadge, LegalityPanel } from '../components/Legality'
import { PokemonCard } from '../components/PokemonCard'
import { UploadArea } from '../components/UploadArea'
import { Button, Card } from '../components/ui'
import { ApiError, api, spriteUrl } from '../lib/api'
import type { LegalityReport, Pokemon, SaveUpload } from '../lib/types'

const linkClass = 'rounded-lg px-3 py-1.5 text-sm text-sky-700 hover:bg-sky-50 dark:text-sky-400 dark:hover:bg-slate-800'

export default function PokemonPage() {
  const { t, i18n } = useTranslation()
  const [pokemon, setPokemon] = useState<Pokemon[] | null>(null)
  const [reports, setReports] = useState<Record<string, LegalityReport>>({})

  const load = useCallback(() => {
    api.get<Pokemon[]>('/api/pokemon').then(setPokemon).catch(() => setPokemon([]))
  }, [])
  useEffect(load, [load])

  async function toggleReport(id: string) {
    if (reports[id]) {
      setReports(({ [id]: _removed, ...rest }) => rest)
      return
    }
    const report = await api.get<LegalityReport>(`/api/pokemon/${id}/legality?lang=${i18n.language}`)
    setReports((r) => ({ ...r, [id]: report }))
  }

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold">{t('pokemon.title')}</h1>
        <p className="text-sm text-slate-500">{t('pokemon.subtitle')}</p>
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <UploadArea onImported={load} />
        </Card>
        <SaveImportCard onImported={load} />
      </div>
      {pokemon === null && <p className="text-slate-500">{t('common.loading')}</p>}
      {pokemon?.length === 0 && <p className="text-slate-500">{t('pokemon.empty')}</p>}
      <div className="grid gap-3 md:grid-cols-2">
        {pokemon?.map((p) => (
          <div key={p.id} className="space-y-2">
            <PokemonCard
              pokemon={p}
              actions={
                <div className="flex flex-col items-end gap-1">
                  <Link to={`/criar/${p.id}`} className={linkClass}>
                    {t('pokemon.editInBuilder')}
                  </Link>
                  <a href={`/api/pokemon/${p.id}/file`} className={linkClass}>
                    {t('pokemon.download')}
                  </a>
                  <button type="button" className={linkClass} onClick={() => void toggleReport(p.id)}>
                    {reports[p.id] ? t('legality.hide') : t('legality.view')}
                  </button>
                </div>
              }
            />
            {reports[p.id] && (
              <Card>
                <LegalityPanel report={reports[p.id]} />
              </Card>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}

function SaveImportCard({ onImported }: { onImported: () => void }) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const [upload, setUpload] = useState<SaveUpload | null>(null)
  const [selected, setSelected] = useState<number[]>([])
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null)
  const [busy, setBusy] = useState(false)

  async function send(file: File) {
    setBusy(true)
    setMessage(null)
    const form = new FormData()
    form.append('file', file, file.name)
    try {
      const result = await api.post<SaveUpload>('/api/pokemon/sav', form)
      setUpload(result)
      setSelected([])
    } catch (e) {
      const code = e instanceof ApiError ? e.errors[0] : undefined
      setMessage({ ok: false, text: code ? t(`pokemon.savErrors.${code}`, { defaultValue: t('common.error') }) : t('common.error') })
    } finally {
      setBusy(false)
    }
  }

  async function importSelected() {
    if (!upload) return
    setBusy(true)
    try {
      const results = await api.post<unknown[]>(`/api/pokemon/sav/${upload.token}/import`, { indices: selected })
      setMessage({ ok: true, text: t('pokemon.savDone', { count: results.length }) })
      setUpload(null)
      onImported()
    } catch (e) {
      const code = e instanceof ApiError ? e.code : undefined
      setMessage({ ok: false, text: code ? t(`pokemon.savErrors.${code}`, { defaultValue: t('common.error') }) : t('common.error') })
    } finally {
      setBusy(false)
    }
  }

  const toggle = (index: number) =>
    setSelected((s) => (s.includes(index) ? s.filter((i) => i !== index) : [...s, index]))

  return (
    <Card className="space-y-3">
      <h2 className="font-semibold">{t('pokemon.importSav')}</h2>
      <p className="text-sm text-slate-500">{t('pokemon.savIntro')}</p>
      <input
        ref={input}
        type="file"
        accept=".sav,.srm,.dsv,.fla"
        hidden
        onChange={(e) => {
          if (e.target.files?.[0]) void send(e.target.files[0])
          e.target.value = ''
        }}
      />
      <Button variant="secondary" disabled={busy} onClick={() => input.current?.click()}>
        {t('pokemon.savChoose')}
      </Button>
      {message && <p className={`text-sm ${message.ok ? 'text-emerald-700 dark:text-emerald-400' : 'text-red-700 dark:text-red-400'}`}>{message.text}</p>}
      {upload && (
        <div className="space-y-2">
          <p className="text-sm font-medium">{t('pokemon.savHeader', { game: upload.game, trainer: upload.trainerName, tid: upload.tid })}</p>
          <Button variant="ghost" onClick={() => setSelected(upload.entries.map((e) => e.index))}>
            {t('pokemon.savSelectAll')}
          </Button>
          <ul className="max-h-72 space-y-1 overflow-y-auto">
            {upload.entries.map((e) => (
              <li key={e.index}>
                <label className="flex items-center gap-2 rounded-lg px-2 py-1 text-sm hover:bg-slate-50 dark:hover:bg-slate-800">
                  <input type="checkbox" checked={selected.includes(e.index)} onChange={() => toggle(e.index)} />
                  <img src={spriteUrl(e.summary.species)} alt="" className="h-8 w-8 [image-rendering:pixelated]" />
                  <span className="flex-1">
                    {e.summary.nickname} · {t('pokemon.level', { level: e.summary.level })}
                    <span className="block text-xs text-slate-500">
                      {e.place === 'party' ? t('pokemon.savParty', { n: e.slot + 1 }) : t('pokemon.savBox', { box: e.box + 1, slot: e.slot + 1 })}
                    </span>
                  </span>
                  <LegalityBadge status={e.legality} />
                </label>
              </li>
            ))}
          </ul>
          <Button disabled={selected.length === 0 || busy} onClick={() => void importSelected()}>
            {t('pokemon.savImport', { count: selected.length })}
          </Button>
        </div>
      )}
    </Card>
  )
}
