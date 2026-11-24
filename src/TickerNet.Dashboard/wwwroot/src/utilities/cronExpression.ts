/**
 * NCrontab accepts 0 for Sunday but not 7, while the editor's cron parser
 * accepts both. Rewrite a plain 7 in the day-of-week field to 0 so a saved
 * expression means what the preview showed.
 */
export function normalizeCronExpression(expression: string): string {
  const parts = String(expression || '').trim().split(/\s+/)
  if (parts.length !== 6) return expression
  parts[5] = parts[5]
    .split(',')
    .map((part) => (part === '7' ? '0' : part))
    .join(',')
  return parts.join(' ')
}
