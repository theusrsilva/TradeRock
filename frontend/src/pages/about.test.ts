import { describe, expect, it } from 'vitest'
import notices from '../../../THIRD-PARTY-NOTICES.md?raw'
import { noticeRows } from './AboutPage'

describe('créditos da tela Sobre', () => {
  it('lê a tabela do THIRD-PARTY-NOTICES.md', () => {
    const rows = noticeRows(notices)
    expect(rows.length).toBeGreaterThan(10)
    expect(rows.every((r) => r.length === 3)).toBe(true)
    const names = rows.map((r) => r[0]).join(' ')
    for (const project of ['frlg-ldn-trade', 'PKHeX.Core', 'Photino.NET', 'iproute2']) expect(names).toContain(project)
  })
})
