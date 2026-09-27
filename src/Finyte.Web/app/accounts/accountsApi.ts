import { httpClient } from '../api/httpClient'

export type Account = {
  id: string
  name: string
  originalName: string
  customName: string | null
  currentBalance: number | string
  availableBalance: number | string | null
  currency: string
  createdAt: string
  accountType: string
  inferredAccountType: string
  accountTypeOverride: string | null
  defaultIncludeInAnalytics: boolean
  includeInAnalyticsOverride: boolean | null
  transferNicknames: string[]
  includeInAnalytics: boolean
  isProviderManaged: boolean
  productName: string | null
  productCategory: string | null
  balanceAsOf: string | null
  preferencesVersion: number
  manualBalanceVersion: number
}

export const accountTypes = [
  ['everyday', 'Everyday'], ['savings', 'Savings'], ['credit-card', 'Credit card'],
  ['home-loan', 'Home loan'], ['offset', 'Offset'], ['loan', 'Other loan'],
  ['investment', 'Investment'], ['term-deposit', 'Term deposit'], ['other', 'Other / unclassified'],
] as const

export function accountTypeLabel(type: string) {
  return accountTypes.find(x => x[0] === type)?.[1] ?? 'Other / unclassified'
}

export function defaultAnalytics(type: string) {
  return !['home-loan', 'loan', 'investment', 'term-deposit'].includes(type)
}

export function getAccounts() {
  return httpClient<Account[]>({ method: 'GET', url: '/api/accounts' })
}
