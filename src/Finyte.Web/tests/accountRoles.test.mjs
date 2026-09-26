import assert from 'node:assert/strict'
import { test } from 'node:test'
import { initialRoles, suggestedRole } from '../app/pay-cycles/accountRoles.ts'

const account = (id, accountType, includeInAnalytics = true) => ({ id, accountType, includeInAnalytics })

test('a savings account is suggested as a destination rather than a tracked account', () => {
  assert.equal(suggestedRole(account('a', 'savings')), 'savings')
  assert.equal(suggestedRole(account('a', 'term-deposit')), 'savings')
})

test('everyday accounts are tracked and dashboard-excluded accounts are left out', () => {
  assert.equal(suggestedRole(account('a', 'everyday')), 'spending')
  assert.equal(suggestedRole(account('a', 'credit-card')), 'spending')
  assert.equal(suggestedRole(account('a', 'home-loan', false)), 'excluded')
})

test('an unclassified account gets no suggestion so the user has to confirm it', () => {
  assert.equal(suggestedRole(account('a', 'other')), 'excluded')
  assert.equal(suggestedRole(account('a', 'other', false)), 'excluded')
})

test('suggested roles leave a savings account available as a destination', () => {
  const accounts = [account('everyday', 'everyday'), account('savings', 'savings')]
  const roles = initialRoles(accounts)
  assert.deepEqual(roles, { everyday: 'spending', savings: 'savings' })
  assert.notEqual(roles.savings, roles.everyday)
})

test('an existing profile keeps the roles it was saved with', () => {
  const accounts = [account('tracked', 'everyday'), account('destination', 'everyday'), account('dropped', 'savings')]
  const profile = { accountIds: ['tracked'], savingsAccountIds: ['destination'] }
  assert.deepEqual(initialRoles(accounts, profile), { tracked: 'spending', destination: 'savings', dropped: 'excluded' })
})
