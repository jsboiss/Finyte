// Financial dates are calendar days (yyyy-mm-dd), never instants. The API buckets on the
// household calendar, so defaults use the browser's local day rather than the UTC day.
const pad = (value: number) => String(value).padStart(2, '0')

export function localDate(date: Date) {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

export function todayDate() {
  return localDate(new Date())
}

export function currentMonth() {
  return todayDate().slice(0, 7)
}

export function shiftDate(date: string, days: number) {
  const [year, month, day] = date.split('-').map(Number)
  const shifted = new Date(Date.UTC(year, month - 1, day + days))
  return `${shifted.getUTCFullYear()}-${pad(shifted.getUTCMonth() + 1)}-${pad(shifted.getUTCDate())}`
}

export function dateLabel(date: string, options: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short', year: 'numeric' }) {
  return new Date(`${date}T00:00:00Z`).toLocaleDateString('en-AU', { timeZone: 'UTC', ...options })
}
