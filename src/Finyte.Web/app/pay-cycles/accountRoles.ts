import type { Account } from '../accounts/accountsApi'
import type { PayCycleProfile } from './payCyclesApi'

export type AccountRole = 'spending' | 'savings' | 'excluded'

const savingsDestinationTypes = ['savings', 'term-deposit']

export function suggestedRole(account: Pick<Account, 'accountType' | 'includeInAnalytics'>): AccountRole {
  if (savingsDestinationTypes.includes(account.accountType)) {
    return 'savings'
  }
  if (account.accountType === 'other') {
    return 'excluded'
  }
  return account.includeInAnalytics ? 'spending' : 'excluded'
}

export function initialRoles(accounts: Account[], profile?: PayCycleProfile): Record<string, AccountRole> {
  return Object.fromEntries(accounts.map(x => [x.id, profile ? savedRole(profile, x.id) : suggestedRole(x)]))
}

function savedRole(profile: PayCycleProfile, accountId: string): AccountRole {
  if (profile.accountIds.includes(accountId)) {
    return 'spending'
  }
  return profile.savingsAccountIds.includes(accountId) ? 'savings' : 'excluded'
}
