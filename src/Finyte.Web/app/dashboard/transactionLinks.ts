import { defaultTransactionFilters, transactionSearchParams } from '../transactions/transactionSearch'
import type { DashboardDateRange, OverviewResponse } from './types'

export function monthRange(month: string): DashboardDateRange {
  const [year, monthNumber] = month.split('-').map(Number)
  const lastDay = new Date(Date.UTC(year, monthNumber, 0)).getUTCDate()
  return { from: `${month}-01`, to: `${month}-${lastDay}` }
}

export function categoryTransactionLink(overview: OverviewResponse, category: string, range = monthRange(overview.monthKey)) {
  const isUncategorised = category === 'Uncategorised'
  const filters = {
    ...defaultTransactionFilters, ...range, accountId: overview.scope.accountId ?? '',
    postedOnly: true, analyticsOnly: !overview.scope.accountId, direction: 'debit',
    internalTransfers: overview.includeInternalTransfers ? 'include' : 'exclude',
    category: isUncategorised ? '' : category, uncategorised: isUncategorised,
  }
  return `/transactions?${transactionSearchParams(1, 25, filters)}`
}

export function transactionLink(overview: OverviewResponse, direction = 'all', range = monthRange(overview.monthKey), tagId?: string | null) {
  const filters = {
    ...defaultTransactionFilters, ...range, accountId: overview.scope.accountId ?? '',
    postedOnly: true, analyticsOnly: !overview.scope.accountId, direction,
    internalTransfers: overview.includeInternalTransfers ? 'include' : 'exclude',
    tagIds: tagId ? [tagId] : [], untagged: tagId === null,
  }
  return `/transactions?${transactionSearchParams(1, 25, filters)}`
}
