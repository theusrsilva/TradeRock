import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { PokemonCard } from '../components/PokemonCard'
import { UploadArea } from '../components/UploadArea'
import { Card } from '../components/ui'
import { api } from '../lib/api'
import type { Pokemon } from '../lib/types'

export default function PokemonPage() {
  const { t } = useTranslation()
  const [pokemon, setPokemon] = useState<Pokemon[] | null>(null)

  const load = useCallback(() => {
    api.get<Pokemon[]>('/api/pokemon').then(setPokemon).catch(() => setPokemon([]))
  }, [])
  useEffect(load, [load])

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold">{t('pokemon.title')}</h1>
        <p className="text-sm text-slate-500">{t('pokemon.subtitle')}</p>
      </div>
      <Card>
        <UploadArea onImported={load} />
      </Card>
      {pokemon === null && <p className="text-slate-500">{t('common.loading')}</p>}
      {pokemon?.length === 0 && <p className="text-slate-500">{t('pokemon.empty')}</p>}
      <div className="grid gap-3 md:grid-cols-2">
        {pokemon?.map((p) => (
          <PokemonCard
            key={p.id}
            pokemon={p}
            actions={
              <a
                href={`/api/pokemon/${p.id}/file`}
                className="rounded-lg px-3 py-1.5 text-sm text-sky-700 hover:bg-sky-50 dark:text-sky-400 dark:hover:bg-slate-800"
              >
                {t('pokemon.download')}
              </a>
            }
          />
        ))}
      </div>
    </div>
  )
}
