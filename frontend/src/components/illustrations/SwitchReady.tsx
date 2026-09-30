/** Esquema: Centro Pokémon (2º andar) → balcão do Direct Corner → "ser Leader". Sem imagens do jogo. */
export function SwitchReady({ labels }: { labels: [string, string, string] }) {
  return (
    <svg viewBox="0 0 360 120" role="img" aria-label={labels.join(' → ')} className="w-full max-w-lg text-slate-700 dark:text-slate-200">
      <defs>
        <marker id="arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto">
          <path d="M0,0 L10,5 L0,10 z" fill="currentColor" />
        </marker>
      </defs>
      {/* Centro Pokémon */}
      <g transform="translate(10,14)">
        <rect x="0" y="20" width="90" height="56" rx="6" className="fill-red-100 stroke-red-500 dark:fill-red-950" strokeWidth="2" />
        <path d="M-4 22 L45 0 L94 22 z" className="fill-red-500" />
        <rect x="36" y="48" width="18" height="28" className="fill-sky-200 stroke-sky-600 dark:fill-sky-900" strokeWidth="2" />
        <text x="45" y="42" textAnchor="middle" fontSize="12" fontWeight="700" fill="currentColor">2F</text>
      </g>
      <line x1="108" y1="60" x2="132" y2="60" stroke="currentColor" strokeWidth="2" markerEnd="url(#arrow)" />
      {/* Balcão */}
      <g transform="translate(138,24)">
        <rect x="0" y="30" width="84" height="26" rx="4" className="fill-amber-100 stroke-amber-600 dark:fill-amber-950" strokeWidth="2" />
        <circle cx="42" cy="16" r="10" className="fill-slate-200 stroke-slate-500 dark:fill-slate-700" strokeWidth="2" />
        <path d="M30 30 Q42 18 54 30" className="fill-slate-200 stroke-slate-500 dark:fill-slate-700" strokeWidth="2" />
      </g>
      <line x1="228" y1="60" x2="252" y2="60" stroke="currentColor" strokeWidth="2" markerEnd="url(#arrow)" />
      {/* Leader */}
      <g transform="translate(258,26)">
        <rect x="0" y="4" width="92" height="56" rx="10" className="fill-slate-100 stroke-slate-500 dark:fill-slate-800" strokeWidth="2" />
        <text x="46" y="38" textAnchor="middle" fontSize="14" fontWeight="700" className="fill-sky-600 dark:fill-sky-400">LEADER</text>
      </g>
      {labels.map((label, i) => (
        <text key={i} x={[55, 180, 304][i]} y="112" textAnchor="middle" fontSize="11" fill="currentColor">
          {label}
        </text>
      ))}
    </svg>
  )
}
