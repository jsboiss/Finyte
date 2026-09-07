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
}

export const defaultTransactionFilters: TransactionFilters = {
  accountId: '', from: '', to: '', search: '', category: '', tagIds: [],
  tagMatch: 'any', untagged: false, minAmount: '', maxAmount: '', currency: '', sort: '-date',
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
  return params
}
