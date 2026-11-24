import { describe, expect, it } from 'vitest'
import { normalizeCronExpression } from '../utilities/cronExpression'

describe('normalizeCronExpression', () => {
  it('maps a plain 7 in day-of-week to Sunday 0', () => {
    expect(normalizeCronExpression('0 0 9 * * 7')).toBe('0 0 9 * * 0')
    expect(normalizeCronExpression('0 0 9 * * 1,7')).toBe('0 0 9 * * 1,0')
  })

  it('leaves other fields and forms alone', () => {
    expect(normalizeCronExpression('0 0 9 * * 0')).toBe('0 0 9 * * 0')
    expect(normalizeCronExpression('0 0 9 * * MON')).toBe('0 0 9 * * MON')
    expect(normalizeCronExpression('*/7 0 9 * * 1')).toBe('*/7 0 9 * * 1')
    expect(normalizeCronExpression('0 7 9 * *')).toBe('0 7 9 * *')
  })
})
