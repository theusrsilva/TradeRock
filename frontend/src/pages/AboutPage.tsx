import ReactMarkdown from 'react-markdown'
import { useTranslation } from 'react-i18next'
import { ContentFile } from '../components/Markdown'
import { useState } from 'react'
import { Icon, Logo } from '../components/ui'
import { useAppInfo } from '../lib/appInfo'
import { UpdateCard } from './UpdateCard'
import notices from '../../../THIRD-PARTY-NOTICES.md?raw'

export const RELEASES_URL = 'https://github.com/theusrsilva/TradeRock/releases'

/** Linhas da tabela do THIRD-PARTY-NOTICES.md (projeto, uso, licença), sem depender de remark-gfm. */
export function noticeRows(text: string): string[][] {
  return text
    .split('\n')
    .filter((l) => l.startsWith('|'))
    .map((l) =>
      l
        .slice(1, l.trimEnd().endsWith('|') ? l.trimEnd().length - 1 : undefined)
        .split('|')
        .map((c) => c.trim()),
    )
    .filter((cells) => !cells.every((c) => /^:?-+:?$/.test(c)))
    .slice(1) // cabeçalho
}

const inline = { p: ({ children }: { children?: React.ReactNode }) => <>{children}</> }
const linkClass = '[&_a]:text-accent [&_a]:underline [&_code]:rounded [&_code]:bg-surface2 [&_code]:px-1'

/** Sobre: versão, licença, oferta do código-fonte e créditos. */
export default function AboutPage() {
  const { t } = useTranslation()
  const info = useAppInfo()
  const rows = noticeRows(notices)
  const [credits, setCredits] = useState(false)
  const os = info?.os === 'windows' ? 'Windows' : info?.os === 'linux' ? 'Linux' : null
  const facts = [
    { icon: 'lock', title: t('ui.about.keysTitle'), sub: t('ui.about.keysSub') },
    { icon: 'wifi_off', title: t('ui.about.offlineTitle'), sub: t('ui.about.offlineSub') },
    { icon: 'code', title: t('ui.about.freeTitle'), sub: t('ui.about.freeSub') },
  ]
  return (
    <div className="flex max-w-[820px] flex-col gap-4">
      <h1 className="font-display text-[23px] font-bold md:text-[28px]">{t('about.link')}</h1>
      <div className="flex flex-col gap-3 rounded-xl border border-line bg-surface p-[22px]">
        <div className="flex items-center gap-3.5">
          <Logo size={48} />
          <div className="flex-1">
            <div className="font-display text-[22px] font-bold">TradeRock</div>
            <div className="text-[13px] text-muted">
              {t('about.version', { version: info?.version.split('+')[0] ?? '…' })}
              {os && ` · ${os}`} · AGPL-3.0
            </div>
          </div>
        </div>
        <p className="text-[14.5px]">{t('ui.about.intro')}</p>
        <div className="grid gap-2.5 [grid-template-columns:repeat(auto-fit,minmax(200px,1fr))]">
          {facts.map((f) => (
            <div key={f.icon} className="flex flex-col gap-1 rounded-lg bg-surface2 p-3">
              <Icon name={f.icon} className="text-[22px] text-accent" />
              <b className="text-sm">{f.title}</b>
              <span className="text-[13px] text-muted">{f.sub}</span>
            </div>
          ))}
        </div>
        <p className="text-sm">
          {t('about.releases')}{' '}
          <a href={RELEASES_URL} target="_blank" rel="noreferrer" className="break-all text-accent underline">
            {RELEASES_URL}
          </a>
        </p>
        <details className="text-sm">
          <summary className="cursor-pointer font-semibold text-accent">{t('ui.about.license')}</summary>
          <div className="mt-2">
            <ContentFile name="sobre" />
          </div>
        </details>
      </div>
      <UpdateCard />
      <div className="rounded-xl border border-line bg-surface">
        <button
          type="button"
          aria-expanded={credits}
          onClick={() => setCredits(!credits)}
          className="flex w-full cursor-pointer items-center gap-2 px-5 py-4 text-left"
        >
          <span className="flex-1 text-base font-extrabold">{t('about.credits')}</span>
          <span className="text-[13px] text-muted">{t('ui.about.projects', { count: rows.length })}</span>
          <Icon name={credits ? 'expand_less' : 'expand_more'} className="text-[22px] text-muted" />
        </button>
        {credits && (
          <div className="px-5 pb-3.5">
            <p className="pb-2 text-[13px] text-muted">{t('about.creditsHelp')}</p>
            <div className={`text-[13.5px] ${linkClass}`}>
              {rows.map((cells) => (
                <div key={cells[0]} className="grid gap-x-3 gap-y-0.5 border-t border-line py-2.5 sm:grid-cols-[minmax(0,1.2fr)_minmax(0,1.4fr)_minmax(0,1fr)]">
                  <span className="font-semibold">
                    <ReactMarkdown components={inline}>{cells[0]}</ReactMarkdown>
                  </span>
                  <span>
                    <ReactMarkdown components={inline}>{cells[1]}</ReactMarkdown>
                  </span>
                  <span className="text-muted">
                    <ReactMarkdown components={inline}>{cells[2]}</ReactMarkdown>
                  </span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
