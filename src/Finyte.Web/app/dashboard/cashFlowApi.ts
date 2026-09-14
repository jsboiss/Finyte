import { httpClient } from '../api/httpClient'
import type { CashFlowRangeResponse, DashboardDateRange } from './types'

export function getCashFlowQueryKey(accountId: string | null, range: DashboardDateRange, includeInternalTransfers = false) {
  return ['cash-flow', accountId ?? 'all', range.from, range.to, includeInternalTransfers] as const
}

export async function getCashFlow(accountId: string | null, range: DashboardDateRange, includeInternalTransfers = false) {
  const params = new URLSearchParams({ from: range.from, to: range.to, includeInternalTransfers: String(includeInternalTransfers) })
  if (accountId) {
    params.set('accountId', accountId)
  }

  return httpClient<CashFlowRangeResponse>({
    method: 'GET',
    url: `/api/cash-flow?${params}`,
  })
}

export function createEmptyCashFlow(range: DashboardDateRange, currency: string): CashFlowRangeResponse {
  const from = new Date(`${range.from}T00:00:00`)
  const to = new Date(`${range.to}T00:00:00`)
  const dailyCashFlow: CashFlowRangeResponse['dailyCashFlow'] = []

  for (const date = new Date(from); date <= to; date.setDate(date.getDate() + 1)) {
    dailyCashFlow.push({
      date: formatDate(date),
      day: date.getDate(),
      incomeMinorUnits: 0,
      expenseMinorUnits: 0,
    })
  }

  return { ...range, currency, dailyCashFlow }
}

function formatDate(value: Date) {
  const year = value.getFullYear()
  const month = (value.getMonth() + 1).toString().padStart(2, '0')
  const day = value.getDate().toString().padStart(2, '0')
  return `${year}-${month}-${day}`
}
