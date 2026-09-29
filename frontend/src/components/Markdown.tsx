import ReactMarkdown from 'react-markdown'
import { useTranslation } from 'react-i18next'
import { contentBlocks } from '../lib/content'

const prose =
  'space-y-2 text-sm leading-relaxed [&_ol]:list-decimal [&_ol]:space-y-1 [&_ol]:pl-5 [&_ul]:list-disc [&_ul]:space-y-1 [&_ul]:pl-5 [&_code]:rounded [&_code]:bg-slate-100 [&_code]:px-1 dark:[&_code]:bg-slate-800 [&_strong]:font-semibold'

/** Texto simples em markdown (sem HTML). */
export function MarkdownText({ text, className = '' }: { text: string; className?: string }) {
  return (
    <div className={`${prose} ${className}`}>
      <ReactMarkdown>{text}</ReactMarkdown>
    </div>
  )
}

/** Arquivo de content/<idioma>/<name>.md, com as seções "Saiba mais" expansíveis. */
export function ContentFile({ name }: { name: string }) {
  const { i18n } = useTranslation()
  return (
    <div className="space-y-3">
      {contentBlocks(i18n.language, name).map((block, i) =>
        block.type === 'markdown' ? (
          <MarkdownText key={i} text={block.text} />
        ) : (
          <details key={i} className="rounded-lg border border-slate-200 px-3 py-2 dark:border-slate-700">
            <summary className="cursor-pointer text-sm font-medium text-sky-700 dark:text-sky-400">{block.summary}</summary>
            <MarkdownText text={block.text} className="mt-2 text-slate-600 dark:text-slate-300" />
          </details>
        ),
      )}
    </div>
  )
}
