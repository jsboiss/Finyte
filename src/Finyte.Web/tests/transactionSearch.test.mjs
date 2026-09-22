import assert from 'node:assert/strict'
import { test } from 'node:test'
import { defaultTransactionFilters, readTransactionSearch, readTransactionRouteSearch, transactionRouteSearch, transactionSearchParams } from '../app/transactions/transactionSearch.ts'

test('new searches use positive amounts while old amount bookmarks remain signed', () => {
  assert.equal(readTransactionSearch('').filters.amountMode, 'absolute')
  for (const search of ['minAmount=-100&maxAmount=-20', 'minAmount=20', 'maxAmount=100']) {
    assert.equal(readTransactionSearch(search).filters.amountMode, 'signed')
  }
})

test('positive ranges, direction, sorting and repeated tags survive route navigation', () => {
  const filters = { ...defaultTransactionFilters, search: 'coffee & tea', direction: 'debit', minAmount: '20', maxAmount: '100', sort: '-magnitude', tagIds: ['one', 'two'], tagMatch: 'all', internalTransfers: 'exclude', analyticsOnly: true }
  const route = transactionRouteSearch(3, filters, 'confirmed')
  assert.deepEqual(readTransactionRouteSearch(route), { page: 3, filters })
  assert.equal(route.transferView, 'confirmed')
  assert.deepEqual(readTransactionSearch(transactionSearchParams(3, 25, filters).toString()), { page: 3, filters })
})

test('exact zero and decimal amounts retain both bounds', () => {
  for (const amount of ['0', '25.31']) {
    const filters = { ...defaultTransactionFilters, minAmount: amount, maxAmount: amount }
    assert.deepEqual(readTransactionRouteSearch(transactionRouteSearch(1, filters)), { page: 1, filters })
  }
})

test('legacy signed sorts and bounds keep their meaning when serialized again', () => {
  const parsed = readTransactionSearch('minAmount=-100&maxAmount=-20&sort=amount')
  assert.deepEqual(readTransactionRouteSearch(transactionRouteSearch(parsed.page, parsed.filters)), parsed)
})

test('clearing amount filters removes bounds and resets pagination in the next route', () => {
  const { filters } = readTransactionSearch('page=3&amountMode=absolute&minAmount=20&maxAmount=100&direction=debit')
  const route = transactionRouteSearch(1, { ...filters, minAmount: '', maxAmount: '' })
  assert.equal(route.minAmount, undefined)
  assert.equal(route.maxAmount, undefined)
  assert.equal(readTransactionRouteSearch(route).page, 1)
  assert.equal(readTransactionRouteSearch(route).filters.direction, 'debit')
})
