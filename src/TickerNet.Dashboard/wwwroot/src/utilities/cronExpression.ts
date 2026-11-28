// The dashboard validates cron expressions with cronstrue, which accepts 7 for
// Sunday in the day-of-week field, but the backend parser (NCrontab) only
// accepts 0-6, so saving an expression with 7 fails server-side.
// Normalize a standalone 7 in the day-of-week segment to 0 before saving.
export function normalizeCronExpression(expression: string): string {
  const segments = expression.trim().split(/\s+/)
  if (segments.length !== 6) return expression
  segments[5] = segments[5]
    .split(',')
    .map((part) => (part === '7' ? '0' : part))
    .join(',')
  return segments.join(' ')
}
