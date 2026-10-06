import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
  useSyncExternalStore,
  type ButtonHTMLAttributes,
  type ReactNode,
} from 'react'

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost' | 'outlineDanger'

/** Altura e largura passadas em className valem mais que as do tamanho padrão. */
const overrideSize = (className: string) =>
  className
    .split(/\s+/)
    .map((c) => (/^(h|w|min-w)-/.test(c) && !c.endsWith('!') ? `${c}!` : c))
    .join(' ')

const variants: Record<Variant, string> = {
  primary: 'border border-transparent bg-accent text-accent-ink font-bold hover:brightness-95',
  secondary: 'border border-line bg-surface text-ink font-semibold hover:bg-surface2',
  danger: 'border border-transparent bg-bad text-white font-bold hover:brightness-95',
  outlineDanger: 'border-[1.5px] border-bad bg-transparent text-bad font-bold hover:bg-bad-soft',
  ghost: 'border border-transparent bg-transparent text-muted font-semibold hover:bg-surface2 hover:text-ink',
}

const sizes = {
  sm: 'h-8 px-2.5 text-[13px] gap-1.5',
  md: 'h-10 px-3.5 text-sm gap-2',
  lg: 'h-11 px-5 text-[15px] gap-2',
}

export function Button({
  variant = 'primary',
  size = 'md',
  icon,
  iconAfter,
  className = '',
  children,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; size?: keyof typeof sizes; icon?: string; iconAfter?: string }) {
  return (
    <button
      type="button"
      className={`inline-flex shrink-0 cursor-pointer items-center justify-center rounded-lg whitespace-nowrap transition disabled:cursor-not-allowed disabled:opacity-45 ${sizes[size]} ${variants[variant]} ${overrideSize(className)}`}
      {...props}
    >
      {icon && <Icon name={icon} className="text-[19px]" />}
      {children}
      {iconAfter && <Icon name={iconAfter} className="text-[19px]" />}
    </button>
  )
}

/** Botão só com ícone (fechar, ajuda, ações compactas). */
export function IconButton({
  name,
  label,
  className = '',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { name: string; label: string }) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      className={`inline-flex h-9 w-9 shrink-0 cursor-pointer items-center justify-center rounded-lg border border-line bg-surface text-ink hover:bg-surface2 disabled:cursor-not-allowed disabled:opacity-45 ${overrideSize(className)}`}
      {...props}
    >
      <Icon name={name} className="text-[19px]" />
    </button>
  )
}

export function Icon({ name, className = '', filled = false }: { name: string; className?: string; filled?: boolean }) {
  return (
    <span aria-hidden className={`icon ${className}`} style={filled ? { fontVariationSettings: "'FILL' 1" } : undefined}>
      {name}
    </span>
  )
}

export function Card({ children, className = '', id }: { children: ReactNode; className?: string; id?: string }) {
  return (
    <section id={id} className={`rounded-xl border border-line bg-surface p-5 ${className}`}>
      {children}
    </section>
  )
}

export function PageTitle({ children, actions }: { children: ReactNode; actions?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-2.5">
      <h1 className="flex-1 font-display text-[23px] leading-tight font-bold md:text-[28px]">{children}</h1>
      {actions}
    </div>
  )
}

export type Tone = 'slate' | 'amber' | 'emerald' | 'red' | 'sky' | 'violet' | 'accent'

const tones: Record<Tone, string> = {
  slate: 'bg-surface2 text-muted',
  amber: 'bg-warn-soft text-warn',
  emerald: 'bg-ok-soft text-ok',
  red: 'bg-bad-soft text-bad',
  sky: 'bg-info-soft text-info',
  violet: 'bg-violet-100 text-violet-800 dark:bg-violet-950/60 dark:text-violet-300',
  accent: 'bg-accent-soft text-ink',
}

export function Badge({ children, tone = 'slate', icon }: { children: ReactNode; tone?: Tone; icon?: string }) {
  return (
    <span className={`inline-flex h-6 items-center gap-1 rounded-full px-2.5 text-xs font-bold whitespace-nowrap ${tones[tone]}`}>
      {icon && <Icon name={icon} className="text-[15px]" />}
      {children}
    </span>
  )
}

/** Faixa de aviso com ícone. */
export function Callout({ tone = 'sky', icon, children, className = '' }: { tone?: Tone; icon?: string; children: ReactNode; className?: string }) {
  return (
    <div className={`flex items-start gap-2.5 rounded-lg px-3.5 py-2.5 text-sm ${tones[tone]} font-normal ${className}`}>
      {icon && <Icon name={icon} className="mt-px text-[20px]" />}
      <div className="min-w-0 flex-1">{children}</div>
    </div>
  )
}

export function Field({ label, help, tip, children }: { label: ReactNode; help?: string; tip?: ReactNode; children: ReactNode }) {
  return (
    <label className="flex min-w-0 flex-col gap-1.5 text-sm">
      <span className="flex items-center gap-1.5 font-bold">
        {label}
        {tip && <HelpTip>{tip}</HelpTip>}
      </span>
      {children}
      {help && <span className="block text-[12.5px] text-muted">{help}</span>}
    </label>
  )
}

export const inputClass =
  'w-full min-w-0 h-11 rounded-lg border border-line bg-bg px-3 text-[15px] text-ink outline-none placeholder:text-muted/70 focus:border-accent disabled:opacity-60'

export const selectClass = `${inputClass} px-2.5!`

export function Modal({ title, children, actions, wide = false }: { title: string; children: ReactNode; actions: ReactNode; wide?: boolean }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/45 p-4" role="dialog" aria-modal="true">
      <div className={`flex max-h-[92vh] w-full flex-col gap-3 overflow-y-auto rounded-xl bg-surface p-5 shadow-xl sm:p-6 ${wide ? 'max-w-2xl' : 'max-w-md'}`}>
        <h2 className="font-display text-xl font-bold">{title}</h2>
        <div className="text-sm text-ink">{children}</div>
        <div className="flex flex-wrap justify-end gap-2 pt-1">{actions}</div>
      </div>
    </div>
  )
}

/** Interruptor liga/desliga. */
export function Toggle({ on, onChange, label, disabled = false, small = false }: { on: boolean; onChange: (on: boolean) => void; label?: ReactNode; disabled?: boolean; small?: boolean }) {
  const w = small ? 'h-5 w-9' : 'h-6 w-11'
  const knob = small ? 'h-4 w-4' : 'h-5 w-5'
  const left = on ? (small ? 'left-[18px]' : 'left-[22px]') : 'left-0.5'
  return (
    <button
      type="button"
      role="switch"
      aria-checked={on}
      disabled={disabled}
      onClick={() => onChange(!on)}
      className="inline-flex cursor-pointer items-center gap-2.5 text-sm font-semibold disabled:cursor-not-allowed disabled:opacity-50"
    >
      <span className={`relative block shrink-0 rounded-full transition-colors ${w} ${on ? 'bg-accent' : 'bg-line'}`}>
        <span className={`absolute top-0.5 rounded-full bg-white shadow transition-all ${knob} ${left}`} />
      </span>
      {label}
    </button>
  )
}

/** Escolha entre poucas opções (pílulas lado a lado). */
export function Segmented<T extends string>({ value, options, onChange, className = '' }: { value: T; options: { value: T; label: ReactNode }[]; onChange: (v: T) => void; className?: string }) {
  return (
    <div className={`flex rounded-lg bg-surface2 p-[3px] ${className}`} role="radiogroup">
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          role="radio"
          aria-checked={value === o.value}
          onClick={() => onChange(o.value)}
          className={`flex-1 cursor-pointer rounded-md px-3 py-1.5 text-[13.5px] font-semibold whitespace-nowrap ${value === o.value ? 'bg-surface text-ink shadow-sm' : 'text-muted'}`}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}

// ---- Celular ----

const mobileQuery = '(max-width: 759px)'

function subscribeMobile(cb: () => void) {
  const m = window.matchMedia(mobileQuery)
  m.addEventListener('change', cb)
  return () => m.removeEventListener('change', cb)
}

/** Tela de celular (menos de 760 px): menu embaixo, painéis que sobem de baixo. */
export function useIsMobile() {
  return useSyncExternalStore(
    subscribeMobile,
    () => window.matchMedia(mobileQuery).matches,
    () => false,
  )
}

/**
 * Ajuda (?): no computador abre ao passar o mouse (ou ao focar com o teclado); no celular, abre e fecha
 * com toque e aparece como uma faixa acima do menu de baixo.
 */
export function HelpTip({ children, label = '?' }: { children: ReactNode; label?: string }) {
  const mobile = useIsMobile()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLSpanElement>(null)

  useEffect(() => {
    if (!open || !mobile) return
    const close = (e: PointerEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', close)
    return () => document.removeEventListener('pointerdown', close)
  }, [open, mobile])

  return (
    <span ref={ref} className="relative inline-flex align-middle" onMouseEnter={() => !mobile && setOpen(true)} onMouseLeave={() => !mobile && setOpen(false)}>
      <button
        type="button"
        aria-label={label === '?' ? 'Ajuda' : label}
        aria-expanded={open}
        onClick={(e) => {
          e.preventDefault()
          e.stopPropagation()
          if (mobile) setOpen((o) => !o)
        }}
        onFocus={() => !mobile && setOpen(true)}
        onBlur={() => !mobile && setOpen(false)}
        className="flex h-[22px] w-[22px] cursor-pointer items-center justify-center rounded-full border border-line bg-surface p-0 text-xs font-extrabold text-muted"
      >
        ?
      </button>
      {open && (
        <span
          role="tooltip"
          className={`z-[35] rounded-lg bg-ink px-3 py-2.5 text-left text-[13px] leading-[1.45] font-medium tracking-normal normal-case text-bg shadow-[0_8px_24px_rgba(0,0,0,.2)] ${
            mobile ? 'fixed right-4 bottom-[84px] left-4' : 'absolute top-7 -left-2.5 w-[min(280px,72vw)]'
          }`}
        >
          {children}
        </span>
      )}
    </span>
  )
}

// ---- Avisos rápidos ----

const ToastContext = createContext<(message: string) => void>(() => undefined)

export function ToastProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<string | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined)
  const show = useCallback((m: string) => {
    clearTimeout(timer.current)
    setMessage(m)
    timer.current = setTimeout(() => setMessage(null), 2400)
  }, [])
  return (
    <ToastContext.Provider value={show}>
      {children}
      {message && (
        <div
          role="status"
          className="fixed bottom-[84px] left-1/2 z-[55] flex -translate-x-1/2 items-center gap-2.5 rounded-lg bg-ink px-4 py-3 font-semibold text-bg shadow-[0_8px_30px_rgba(0,0,0,.25)] md:bottom-7"
        >
          <Icon name="check_circle" />
          {message}
        </div>
      )}
    </ToastContext.Provider>
  )
}

export function useToast() {
  return useContext(ToastContext)
}

/** Pokébola do logotipo. */
export function Logo({ size = 30 }: { size?: number }) {
  const inner = Math.round(size / 3)
  return (
    <span
      aria-hidden
      className="relative block shrink-0 rounded-full border-2 border-[#2a2a2a]"
      style={{ width: size, height: size, background: 'linear-gradient(#e2463c 0 46%, #2a2a2a 46% 54%, #f4f4f4 54%)' }}
    >
      <span
        className="absolute top-1/2 left-1/2 rounded-full border-2 border-[#2a2a2a] bg-[#f4f4f4]"
        style={{ width: inner, height: inner, margin: `-${inner / 2}px 0 0 -${inner / 2}px` }}
      />
    </span>
  )
}
