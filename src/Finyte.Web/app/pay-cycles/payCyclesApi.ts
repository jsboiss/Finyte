import { httpClient } from '../api/httpClient'

export type PayCycleProfile = {
  id: string; name: string; frequency: string; anchorDate: string; currency: string; expectedIncome: number | null
  accountIds: string[]; savingsAccountIds: string[]; version: number; updatedAt: string
}
export type PayCycleBreakdown = {
  profile: PayCycleProfile; from: string; to: string; observedThrough: string | null
  previousDate: string | null; nextDate: string | null; periodStatus: string; dateBasis: string
  accounts: { id: string; name: string; currency: string; includeInAnalytics: boolean }[]
  savingsAccounts: { id: string; name: string }[]; missingAccountIds: string[]
  totals: { externalCredits: number; spending: number; savingsTransfersOut: number; savingsTransfersIn: number; netSavingsTransfers: number
    otherTransfersOut: number; transfersIn: number; withinScopeTransfers: number; netMovement: number; expectedIncomeDifference: number | null; transactionCount: number }
  spendingCategories: { name: string; amount: number; transactionCount: number }[]
  undatedTransactionCount: number; unpostedTransactionCount: number; otherCurrencyTransactionCount: number
  transactions: { page: number; pageSize: number; totalCount: number; kind: string | null; items: {
    id: string; accountId: string; accountName: string; description: string | null; merchantName: string | null
    amount: number; postedAt: string; kind: string; category: string
  }[] }
}
export const kinds = [
  ['external-credit', 'External credits'], ['spending', 'Spending'], ['savings-out', 'To savings'], ['savings-in', 'From savings'],
  ['transfer-out', 'Other transfers out'], ['transfer-in', 'Other transfers in'], ['within-scope', 'Between selected accounts'], ['zero', 'Zero amount'],
] as const
export const getPayCycles = () => httpClient<PayCycleProfile[]>({ method: 'GET', url: '/api/pay-cycles' })
export const getBreakdown = (id: string, date: string, page: number, kind: string) => httpClient<PayCycleBreakdown>({
  method: 'GET', url: `/api/pay-cycles/${id}/breakdown`, params: { date, page, pageSize: 25, kind: kind || undefined },
})

export function money(amount: number, currency: string) {
  return new Intl.NumberFormat('en-AU', { style: 'currency', currency }).format(amount)
}
