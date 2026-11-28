import { describe, it, expect } from 'vitest'
import { normalizeCronExpression } from '../utilities/cronExpression'

describe('normalizeCronExpression', () => {
  it('maps a standalone 7 in the day-of-week segment to 0', () => {
    expect(normalizeCronExpression('0 0 12 * * 7')).toBe('0 0 12 * * 0')
  })

  it('leaves 0-6 and named day-of-week values unchanged', () => {
    expect(normalizeCronExpression('0 0 12 * * 0')).toBe('0 0 12 * * 0')
    expect(normalizeCronExpression('0 0 12 * * 1-5')).toBe('0 0 12 * * 1-5')
    expect(normalizeCronExpression('0 0 12 * * SUN')).toBe('0 0 12 * * SUN')
  })

  it('maps 7 inside a day-of-week list to 0', () => {
    expect(normalizeCronExpression('0 0 12 * * 0,7')).toBe('0 0 12 * * 0,0')
  })

  it('does not touch 7 in any other segment', () => {
    expect(normalizeCronExpression('0 0 12 7 * *')).toBe('0 0 12 7 * *')
    expect(normalizeCronExpression('7 0 12 * * *')).toBe('7 0 12 * * *')
  })

  it('returns non-6-segment expressions unchanged', () => {
    expect(normalizeCronExpression('0 12 * * 7')).toBe('0 12 * * 7')
    expect(normalizeCronExpression('not-a-cron')).toBe('not-a-cron')
  })
})
