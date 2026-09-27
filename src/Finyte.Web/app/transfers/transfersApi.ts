import { httpClient } from '../api/httpClient'

export type TransferRow = {
  id: string; accountId: string; accountName: string; description: string; amount: number; currency: string
  postedAt: string | null; counterpartyAccountId: string | null; counterpartyAccountName: string | null; source: string | null
}
export type TransferPage = { items: TransferRow[]; totalCount: number; page: number; pageSize: number }
export type TransferAction = 'mark' | 'exclude' | 'reset'
export const transferQueryKeys = ['internal-transfers', 'transactions', 'overview', 'cash-flow', 'budgets', 'pay-cycles', 'recurring']

export function reviewTransfer(transactionId: string, action: TransferAction, counterpartyAccountId?: string) {
  return httpClient<void>({ method: 'POST', url: '/api/internal-transfers/review', data: { transactionId, action, counterpartyAccountId: counterpartyAccountId ?? null } })
}

export function reclassifyTransfers() {
  return httpClient<{ changed: number }>({ method: 'POST', url: '/api/internal-transfers/reclassify' })
}

export function transferSourceLabel(source: string | null | undefined) {
  return source === 'manual' ? 'Marked by you' : source === 'excluded' ? 'Marked as not a transfer' : 'Detected from the description'
}
