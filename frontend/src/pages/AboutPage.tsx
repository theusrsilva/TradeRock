import ReactMarkdown from 'react-markdown'
import { useTranslation } from 'react-i18next'
import { ContentFile } from '../components/Markdown'
import { Card } from '../components/ui'
import { useAppInfo } from '../lib/appInfo'
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
const linkClass = '[&_a]:text-sky-700 [&_a]:underline dark:[&_a]:text-sky-400 [&_code]:rounded [&_code]:bg-slate-100 [&_code]:px-1 dark:[&_code]:bg-slate-800'

/** Sobre: versão, licença, oferta do código-fonte e créditos. */
export default function AboutPage() {
  const { t } = useTranslation()
  const info = useAppInfo()
  const rows = noticeRows(notices)
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">{t('about.title')}</h1>
      <Card className="space-y-3">
        <div className="flex items-center gap-3">
          <img src="/icon.svg" alt="" className="h-12 w-12" />
          <div>
            <p className="text-lg font-semibold">TradeRock</p>
            <p className="text-sm text-slate-500">{t('about.version', { version: info?.version ?? '…' })}</p>
          </div>
        </div>
        <ContentFile name="sobre" />
        <p className="text-sm">
          {t('about.releases')}{' '}
          <a href={RELEASES_URL} target="_blank" rel="noreferrer" className="break-all text-sky-700 underline dark:text-sky-400">
            {RELEASES_URL}
          </a>
        </p>
      </Card>
      <Card className="space-y-3">
        <h2 className="font-semibold">{t('about.credits')}</h2>
        <p className="text-sm text-slate-500">{t('about.creditsHelp')}</p>
        <div className="overflow-x-auto">
          <table className={`w-full text-left text-sm ${linkClass}`}>
            <thead className="text-xs uppercase text-slate-500">
              <tr>
                <th className="py-1 pr-3">{t('about.project')}</th>
                <th className="py-1 pr-3">{t('about.use')}</th>
                <th className="py-1">{t('about.license')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((cells) => (
                <tr key={cells[0]} className="border-t border-slate-200 align-top dark:border-slate-700">
                  {cells.slice(0, 3).map((cell, i) => (
                    <td key={i} className={`py-2 ${i < 2 ? 'pr-3' : ''}`}>
                      <ReactMarkdown components={inline}>{cell}</ReactMarkdown>
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  )
}
