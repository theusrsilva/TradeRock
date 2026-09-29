import { describe, expect, it } from 'vitest'
import ptBR from './locales/pt-BR.json'
import en from './locales/en.json'
import { resolveTheme } from './theme'

function keys(obj: object, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([k, v]) =>
    typeof v === 'object' && v !== null ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`],
  )
}

describe('traduções', () => {
  it('inglês tem as mesmas chaves do português', () => {
    expect(keys(en).sort()).toEqual(keys(ptBR).sort())
  })
})

describe('tema', () => {
  it('automático segue o sistema', () => {
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('system', false)).toBe('light')
    expect(resolveTheme('light', true)).toBe('light')
  })
})
