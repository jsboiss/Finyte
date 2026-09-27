import assert from 'node:assert/strict'
import { test } from 'node:test'
import { isAllocatedSpend, tagAllocationMinorUnits } from '../app/transactions/tagAllocation.ts'

test('a two-tag payment splits evenly so the chart share is half the payment', () => {
  assert.equal(tagAllocationMinorUnits(-10000, ['food', 'shared'], 'food'), 5000)
  assert.equal(tagAllocationMinorUnits(-10000, ['food', 'shared'], 'shared'), 5000)
})

test('an indivisible amount gives the remainder to the first tag and still sums to the payment', () => {
  const tagIds = ['food', 'shared', 'travel']
  const allocations = tagIds.map(x => tagAllocationMinorUnits(-10000, tagIds, x))
  assert.deepEqual(allocations, [3334, 3333, 3333])
  assert.equal(allocations.reduce((x, y) => x + y, 0), 10000)
})

test('a single tag takes the whole payment and credits use their magnitude', () => {
  assert.equal(tagAllocationMinorUnits(-2531, ['food'], 'food'), 2531)
  assert.equal(tagAllocationMinorUnits(2531, ['food'], 'food'), 2531)
})

test('a tag the transaction does not carry is allocated nothing', () => {
  assert.equal(tagAllocationMinorUnits(-10000, ['food', 'shared'], 'travel'), 0)
  assert.equal(tagAllocationMinorUnits(-10000, [], 'food'), 0)
})

test('a refund shows no allocated share because the chart counts spending only', () => {
  assert.equal(isAllocatedSpend(10000, 2), false)
  assert.equal(isAllocatedSpend(0, 2), false)
  assert.equal(isAllocatedSpend(-10000, 2), true)
})

test('a single-tag payment shows no allocated share because it already reconciles', () => {
  assert.equal(isAllocatedSpend(-10000, 1), false)
})
