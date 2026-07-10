export function currency(value: number, currencyCode: string) {
  return new Intl.NumberFormat(undefined, {
    currency: currencyCode,
    maximumFractionDigits: 0,
    style: 'currency',
  }).format(value / 100)
}

export function signedCurrency(value: number, currencyCode: string) {
  const formatted = currency(Math.abs(value), currencyCode)
  return value > 0 ? `+${formatted}` : value < 0 ? `-${formatted}` : formatted
}

export function formatDateTime(value: string) {
  return new Date(value).toLocaleString(undefined, { day: 'numeric', hour: 'numeric', minute: '2-digit', month: 'short' })
}

export function formatDate(value: string) {
  return new Date(value).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

export function formatChartDate(value: string) {
  return new Date(`${value}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })
}

export function formatMonth(value: string) {
  return new Date(`${value}-01T00:00:00`).toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
}
