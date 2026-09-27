import { defaultTransactionFilters, transactionSearchParams } from '../transactions/transactionSearch'
import type { DashboardDateRange, OverviewResponse } from './types'

export function monthRange(month: string): DashboardDateRange {
  const [year, monthNumber] = month.split('-').map(Number)
  const lastDay = new Date(Date.UTC(year, monthNumber, 0)).getUTCDate()
  return { from: `${month}-01`, to: `${month}-${lastDay}` }
}

export function transactionLink(overview: OverviewResponse, direction = 'all', range = monthRange(overview.monthKey), tagId?: string | null) {
  const filters = {
    ...defaultTransactionFilters, ...range, accountId: overview.scope.accountId ?? '', accountIds: overview.scope.accountIds ?? [],
    postedOnly: true, analyticsOnly: !overview.scope.accountId && !overview.scope.accountIds?.length, direction, currency: overview.currency,
    internalTransfers: overview.includeInternalTransfers ? 'include' : 'exclude',
    tagIds: tagId ? [tagId] : [], untagged: tagId === null,
  }
  return `/transactions?${transactionSearchParams(1, 25, filters)}`
}
