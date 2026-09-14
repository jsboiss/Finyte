export type TransactionFilters = {
  accountId: string
  from: string
  to: string
  search: string
  category: string
  tagIds: string[]
  tagMatch: 'any' | 'all'
  untagged: boolean
  minAmount: string
  maxAmount: string
  currency: string
  sort: string
  postedOnly: boolean
  analyticsOnly: boolean
  direction: string
  internalTransfers: string
}

export const defaultTransactionFilters: TransactionFilters = {
  accountId: '', from: '', to: '', search: '', category: '', tagIds: [],
  tagMatch: 'any', untagged: false, minAmount: '', maxAmount: '', currency: '', sort: '-date',
  postedOnly: false, analyticsOnly: false, direction: 'all', internalTransfers: 'include',
}

export function transactionSearchParams(page: number, pageSize: number, filters: TransactionFilters) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize), sort: filters.sort })
  for (const key of ['accountId', 'from', 'to', 'search', 'category', 'minAmount', 'maxAmount', 'currency'] as const) {
    if (filters[key]) {
      params.set(key, filters[key])
    }
  }
  for (const tagId of filters.tagIds) {
    params.append('tagIds', tagId)
  }
  if (filters.tagIds.length > 0) {
    params.set('tagMatch', filters.tagMatch)
  }
  if (filters.untagged) {
    params.set('untagged', 'true')
  }
  params.set('direction', filters.direction)
  params.set('internalTransfers', filters.internalTransfers)
  if (filters.postedOnly) params.set('postedOnly', 'true')
  if (filters.analyticsOnly) params.set('analyticsOnly', 'true')
  return params
}

export function readTransactionSearch(search = window.location.search) {
  const params = new URLSearchParams(search)
  const filters = { ...defaultTransactionFilters }
  for (const key of ['accountId', 'from', 'to', 'search', 'category', 'minAmount', 'maxAmount', 'currency', 'sort', 'direction', 'internalTransfers'] as const) {
    if (params.has(key)) filters[key] = params.get(key)!
  }
  filters.tagIds = params.getAll('tagIds')
  filters.tagMatch = params.get('tagMatch') === 'all' ? 'all' : 'any'
  filters.untagged = params.get('untagged') === 'true'
  filters.postedOnly = params.get('postedOnly') === 'true'
  filters.analyticsOnly = params.get('analyticsOnly') === 'true'
  const requestedPage = Number(params.get('page'))
  return { page: Number.isSafeInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1, filters }
}

export function readTransactionRouteSearch(search: Record<string, unknown>) {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(search)) {
    for (const item of Array.isArray(value) ? value : [value]) {
      if (item !== undefined && item !== null) { params.append(key, String(item)) }
    }
  }
  return readTransactionSearch(params.toString())
}

export function transactionRouteSearch(page: number, filters: TransactionFilters, transferView?: string): Record<string, unknown> & { transferView?: string } {
  const search: Record<string, unknown> & { transferView?: string } = Object.fromEntries(transactionSearchParams(page, 25, filters))
  if (filters.tagIds.length > 0) { search.tagIds = filters.tagIds }
  if (transferView) { search.transferView = transferView }
  return search
}
