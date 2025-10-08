import { describe, expect, it } from 'vitest'
import { classifyRfid, normalizeRfid, parseRfid, validateRfid } from './rfid'

describe('normalizeRfid', () => {
  it("nettoie les caractères parasites et extrait le suffixe", () => {
    const result = normalizeRfid("1560000#1")
    expect(result.normalized).toBe('1560000')
    expect(result.suffix).toBe('1')
    expect(result.notes).toContain('Suffixe #1 extrait')
  })
})

describe('classifyRfid', () => {
  it('classe correctement les chaînes', () => {
    expect(classifyRfid('123456')).toBe('digits')
    expect(classifyRfid('ABCDEF')).toBe('letters')
    expect(classifyRfid('A1B2C3')).toBe('alnum')
    expect(classifyRfid('1234*56')).toBe('mixed')
  })
})

describe('validateRfid', () => {
  it('valide les formats numériques attendus', () => {
    expect(validateRfid('12345').level).toBe('green')
    expect(validateRfid('1234567').level).toBe('amber')
    expect(validateRfid('123456').level).toBe('red')
  })
})

describe('parseRfid cas réels', () => {
  it("gère le cas avec cédille", () => {
    const { normalized, verdict } = parseRfid("0939241764'ç")
    expect(normalized).toBe('0939241764C')
    expect(verdict.type).toBe('digits')
    expect(verdict.length).toBe(10)
    expect(verdict.level).toBe('amber')
  })

  it('gère un code alphanumérique attendu', () => {
    const { normalized, verdict } = parseRfid('K2.0013252')
    expect(normalized).toBe('K20013252')
    expect(verdict.type).toBe('alnum')
    expect(verdict.length).toBe(9)
    expect(verdict.level).toBe('green')
  })

  it('extraie un suffixe #1 et garde une longueur inhabituelle', () => {
    const { normalized, suffix, verdict } = parseRfid('1560000#1')
    expect(normalized).toBe('1560000')
    expect(suffix).toBe('1')
    expect(verdict.type).toBe('digits')
    expect(verdict.length).toBe(7)
    expect(verdict.level).toBe('amber')
  })

  it('gère un autre suffixe', () => {
    const { normalized, suffix, verdict } = parseRfid('1570000#1')
    expect(normalized).toBe('1570000')
    expect(suffix).toBe('1')
    expect(verdict.type).toBe('digits')
    expect(verdict.length).toBe(7)
    expect(verdict.level).toBe('amber')
  })

  it('nettoie les symboles degré', () => {
    const { normalized, verdict } = parseRfid('2015°02°10')
    expect(normalized).toBe('20150210')
    expect(verdict.type).toBe('digits')
    expect(verdict.length).toBe(8)
    expect(verdict.level).toBe('amber')
  })

  it('retire les espaces internes', () => {
    const { normalized, verdict } = parseRfid('33906 56')
    expect(normalized).toBe('3390656')
    expect(verdict.type).toBe('digits')
    expect(verdict.length).toBe(7)
    expect(verdict.level).toBe('amber')
  })

  it('gère un suffixe avec longueur verte', () => {
    const { normalized, suffix, verdict } = parseRfid('60000#1')
    expect(normalized).toBe('60000')
    expect(suffix).toBe('1')
    expect(verdict.type).toBe('digits')
    expect(verdict.length).toBe(5)
    expect(verdict.level).toBe('green')
  })

  it('accepte un code lettres', () => {
    const { normalized, verdict } = parseRfid('VHP')
    expect(normalized).toBe('VHP')
    expect(verdict.type).toBe('letters')
    expect(verdict.length).toBe(3)
    expect(verdict.level).toBe('green')
  })

  it('valide un EAN-13', () => {
    const { normalized, verdict } = parseRfid('3557191310038')
    expect(normalized).toBe('3557191310038')
    expect(verdict.type).toBe('digits')
    expect(verdict.length).toBe(13)
    expect(verdict.level).toBe('green')
  })

  it('valide un UPC-A', () => {
    const { normalized, verdict } = parseRfid('012345678905')
    expect(normalized).toBe('012345678905')
    expect(verdict.type).toBe('digits')
    expect(verdict.length).toBe(12)
    expect(verdict.level).toBe('green')
  })

  it('tolère un EAN-8 comme amber', () => {
    const { normalized, verdict } = parseRfid('55123457')
    expect(normalized).toBe('55123457')
    expect(verdict.type).toBe('digits')
    expect(verdict.length).toBe(8)
    expect(verdict.level).toBe('amber')
  })

  it('rejette un format mixte', () => {
    const { normalized, verdict } = parseRfid('1234*56')
    expect(normalized).toBe('1234*56')
    expect(verdict.type).toBe('mixed')
    expect(verdict.level).toBe('red')
    expect(verdict.reason).toBeDefined()
  })
})
