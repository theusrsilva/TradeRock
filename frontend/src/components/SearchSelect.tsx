import { useEffect, useMemo, useRef, useState } from 'react'
import { inputClass } from './ui'
import type { Option } from '../lib/types'

/**
 * Lista com busca (para espécies, golpes e itens). Mostra as opções de 60 em 60: rolar até o fim (ou descer com as
 * setas) carrega mais, até a lista toda. Teclado: setas, Enter e Esc.
 */
const PAGE = 60

export function SearchSelect({
  options,
  value,
  onChange,
  placeholder,
  emptyLabel,
  renderIcon,
  invalid = false,
  id,
}: {
  options: Option[]
  value: number | null
  onChange: (value: number | null) => void
  placeholder?: string
  emptyLabel?: string
  renderIcon?: (value: number) => React.ReactNode
  invalid?: boolean
  id?: string
}) {
  const selected = options.find((o) => o.value === value) ?? null
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const [limit, setLimit] = useState(PAGE)
  const box = useRef<HTMLDivElement>(null)

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase()
    return q ? options.filter((o) => o.text.toLowerCase().includes(q) || String(o.value) === q) : options
  }, [options, query])
  const filtered = matches.slice(0, limit)

  // Busca nova ou lista reaberta: volta às primeiras opções.
  useEffect(() => setLimit(PAGE), [query, open])

  useEffect(() => {
    const close = (e: MouseEvent) => {
      if (box.current && !box.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [])

  function choose(v: number | null) {
    onChange(v)
    setOpen(false)
    setQuery('')
  }

  return (
    <div ref={box} className="relative">
      <div className={`flex items-center gap-2 ${inputClass} ${invalid ? 'ring-2 ring-bad' : ''} p-0! pl-2!`}>
        {selected && renderIcon?.(selected.value)}
        <input
          id={id}
          className="w-full bg-transparent px-1 py-2 outline-none"
          placeholder={selected?.text ?? placeholder ?? emptyLabel}
          value={open ? query : selected?.text ?? ''}
          onFocus={() => {
            setOpen(true)
            setActive(0)
          }}
          onChange={(e) => {
            setQuery(e.target.value)
            setOpen(true)
            setActive(0)
          }}
          onKeyDown={(e) => {
            if (e.key === 'ArrowDown') {
              if (active + 1 >= filtered.length) setLimit((l) => l + PAGE)
              setActive((a) => Math.min(a + 1, matches.length - 1))
            }
            else if (e.key === 'ArrowUp') setActive((a) => Math.max(a - 1, 0))
            else if (e.key === 'Enter' && filtered[active]) {
              e.preventDefault()
              choose(filtered[active].value)
            } else if (e.key === 'Escape') setOpen(false)
          }}
        />
      </div>
      {open && (
        <ul
          className="absolute z-20 mt-1 max-h-72 w-full overflow-y-auto rounded-lg border border-line bg-surface py-1 text-sm shadow-lg"
          onScroll={(e) => {
            const el = e.currentTarget
            if (filtered.length < matches.length && el.scrollTop + el.clientHeight >= el.scrollHeight - 80) setLimit((l) => l + PAGE)
          }}
        >
          {emptyLabel !== undefined && (
            <li>
              <button type="button" className="w-full px-3 py-1.5 text-left text-muted hover:bg-surface2" onMouseDown={() => choose(null)}>
                {emptyLabel}
              </button>
            </li>
          )}
          {filtered.map((o, i) => (
            <li key={o.value}>
              <button
                type="button"
                className={`flex w-full items-center gap-2 px-3 py-1.5 text-left hover:bg-surface2 ${
                  i === active ? 'bg-surface2' : ''
                } ${o.value === value ? 'font-semibold' : ''}`}
                onMouseDown={() => choose(o.value)}
              >
                {renderIcon?.(o.value)}
                {o.text}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
