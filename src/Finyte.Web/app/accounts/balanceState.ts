import type { Account } from './accountsApi'

const staleAfterDays = 3

export type BalanceState =
  | { kind: 'current'; asOf: string }
  | { kind: 'stale'; asOf: string; days: number }
  | { kind: 'missing' }

export function balanceState(account: Account, now = new Date()): BalanceState {
  if (!account.balanceAsOf) {
    return { kind: 'missing' }
  }

  const days = Math.floor((now.getTime() - new Date(account.balanceAsOf).getTime()) / 86_400_000)
  return days >= staleAfterDays
    ? { kind: 'stale', asOf: account.balanceAsOf, days }
    : { kind: 'current', asOf: account.balanceAsOf }
}

export function balanceGuidance(account: Account, state: BalanceState) {
  if (state.kind === 'current') {
    return null
  }

  if (state.kind === 'stale') {
    return account.isProviderManaged
      ? { text: `Last updated ${state.days} days ago. Your bank connection may need attention.`, to: '/connections', action: 'Check connection' }
      : { text: `Last updated ${state.days} days ago. Import a newer OFX statement containing a dated balance to update it.`, to: null, action: null }
  }

  return account.isProviderManaged
    ? { text: 'Your bank has not sent a balance for this account yet.', to: '/connections', action: 'Check connection' }
    : { text: 'No balance has been supplied for this account. Import an OFX statement containing a dated balance to see it here.', to: null, action: null }
}

export function hasReportedBalance(account: Account) {
  return account.balanceAsOf !== null
}
