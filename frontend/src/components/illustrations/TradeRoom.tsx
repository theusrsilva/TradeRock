/** Sala de troca vista de cima: a cadeira da ESQUERDA é a do jogador no Switch. */
export function TradeRoom({ leftLabel, rightLabel }: { leftLabel: string; rightLabel: string }) {
  return (
    <svg viewBox="0 0 240 110" role="img" aria-label={`${leftLabel} / ${rightLabel}`} className="w-full max-w-xs text-slate-700 dark:text-slate-200">
      <rect x="80" y="30" width="80" height="44" rx="6" className="fill-amber-100 stroke-amber-600 dark:fill-amber-950" strokeWidth="2" />
      <rect x="40" y="38" width="28" height="28" rx="6" className="fill-sky-400 stroke-sky-700" strokeWidth="3">
        <animate attributeName="opacity" values="1;0.5;1" dur="1.6s" repeatCount="indefinite" />
      </rect>
      <rect x="172" y="38" width="28" height="28" rx="6" className="fill-slate-200 stroke-slate-500 dark:fill-slate-700" strokeWidth="2" />
      <text x="54" y="92" textAnchor="middle" fontSize="11" fontWeight="700" className="fill-sky-700 dark:fill-sky-400">{leftLabel}</text>
      <text x="186" y="92" textAnchor="middle" fontSize="11" fill="currentColor">{rightLabel}</text>
    </svg>
  )
}
