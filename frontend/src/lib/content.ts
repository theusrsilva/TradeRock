// Textos explicativos em markdown (content/<idioma>/...), embutidos no build.
// Convenções: comentários HTML são ignorados; blocos <details><summary>X</summary>...</details>
// viram seções "Saiba mais"; em fases.md, cada "## chave" é uma instrução.

const files = import.meta.glob('../../../content/*/**/*.md', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

export type ContentBlock = { type: 'markdown'; text: string } | { type: 'details'; summary: string; text: string }

function raw(lang: string, name: string): string {
  const find = (l: string) =>
    Object.entries(files).find(([path]) => path.endsWith(`/content/${l}/${name}.md`))?.[1]
  return find(lang) ?? find('pt-BR') ?? ''
}

const stripComments = (text: string) => text.replace(/<!--[\s\S]*?-->/g, '')

export function contentBlocks(lang: string, name: string): ContentBlock[] {
  const text = stripComments(raw(lang, name))
  const blocks: ContentBlock[] = []
  const pattern = /<details>\s*<summary>([\s\S]*?)<\/summary>([\s\S]*?)<\/details>/g
  let last = 0
  for (const match of text.matchAll(pattern)) {
    const before = text.slice(last, match.index).trim()
    if (before) blocks.push({ type: 'markdown', text: before })
    blocks.push({ type: 'details', summary: match[1].trim(), text: match[2].trim() })
    last = match.index + match[0].length
  }
  const rest = text.slice(last).trim()
  if (rest) blocks.push({ type: 'markdown', text: rest })
  return blocks
}

/** Seções "## chave" de um arquivo (ex.: trade/fases). */
export function contentSections(lang: string, name: string): Record<string, string> {
  const text = stripComments(raw(lang, name))
  const sections: Record<string, string> = {}
  for (const part of text.split(/^## /m).slice(1)) {
    const newline = part.indexOf('\n')
    const key = part.slice(0, newline).trim()
    sections[key] = part.slice(newline + 1).trim()
  }
  return sections
}

export function interpolate(text: string, vars: Record<string, string | number>): string {
  return text.replace(/\{(\w+)\}/g, (whole, key: string) => (key in vars ? String(vars[key]) : whole))
}
