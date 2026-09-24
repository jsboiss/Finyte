const minorUnitScale = 100
const numberFormats = new Map<string, Intl.NumberFormat>()

function moneyFormat(currencyCode: string, exact: boolean) {
  const key = `${currencyCode}|${exact}`
  const cached = numberFormats.get(key)
  if (cached) {
    return cached
  }

  let format: Intl.NumberFormat
  try {
    format = new Intl.NumberFormat(undefined, exact
      ? { currency: currencyCode, style: 'currency' }
      : { currency: currencyCode, maximumFractionDigits: 0, style: 'currency' })
  } catch {
    format = new Intl.NumberFormat(undefined, exact
      ? { maximumFractionDigits: 2, minimumFractionDigits: 2 }
      : { maximumFractionDigits: 0 })
  }
  numberFormats.set(key, format)
  return format
}

export function exactCurrency(minorUnits: number, currencyCode: string) {
  return moneyFormat(currencyCode, true).format(minorUnits / minorUnitScale)
}

export function exactAmount(amount: number, currencyCode: string) {
  return moneyFormat(currencyCode, true).format(amount)
}

export function compactCurrency(minorUnits: number, currencyCode: string) {
  return moneyFormat(currencyCode, false).format(minorUnits / minorUnitScale)
}

export function signedCurrency(minorUnits: number, currencyCode: string) {
  return withSign(minorUnits, exactCurrency(Math.abs(minorUnits), currencyCode))
}

export function signedCompactCurrency(minorUnits: number, currencyCode: string) {
  return withSign(minorUnits, compactCurrency(Math.abs(minorUnits), currencyCode))
}

function withSign(minorUnits: number, formatted: string) {
  return minorUnits > 0 ? `+${formatted}` : minorUnits < 0 ? `-${formatted}` : formatted
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
