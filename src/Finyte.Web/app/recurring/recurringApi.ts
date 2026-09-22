import { isAxiosError } from 'axios'
import { httpClient } from '../api/httpClient'
import { exactAmount } from '../shared/formatters'

export const recurringUrl = '/api/recurring-payments'
export const cadences = ['weekly', 'fortnightly', 'monthly', 'quarterly', 'yearly'] as const
export type AliasField = 'merchant' | 'description'
export type AliasInput = { field: AliasField; value: string }
export type Alias = AliasInput & { id: string }
export type Series = {
  id: string; name: string; accountId: string; accountName: string; currency: string
  cadence: string; anchorDate: string; expectedAmount: number; amountMode: string
  state: string; version: number; aliases: Alias[]; nextDueDate: string | null
  nextDueStatus: string; needsReviewCount: number
}
export type SeriesList = { items: Series[]; costs: { currency: string; monthlyEstimate: number; annualEstimate: number; activeSeriesCount: number; variableSeriesCount: number }[] }
export type Range = { from: string; to: string }
export type Page<T> = { items: T[]; totalCount: number; page: number; pageSize: number }
export type Snapshot = {
  id: string; accountId: string; accountName: string; amount: number; currency: string
  postedAt: string | null; merchantName: string | null; description: string | null
  reference: string | null; status: string | null; fingerprint: string
}
export type Discovery = {
  key: string; name: string; accountId: string; accountName: string; currency: string
  cadence: string; anchorDate: string; expectedAmount: number; aliasField: AliasField; aliasValue: string
  transactions: { snapshot: Snapshot; occurrenceDate: string }[]; evidence: string[]; dismissed: boolean
}
export type Occurrence = { date: string; windowFrom: string; windowTo: string; status: string; expectedAmount: number; paidAmount: number | null; transactionId: string | null }
export type Candidate = { snapshot: Snapshot; suggestedOccurrenceDate: string; aliasMatch: boolean; amountChanged: boolean; evidence: string[]; decisionStatus: string | null; currentlyAssignedSeriesId: string | null; ranking: { version: string; score: number; confidence: string; matchKind: string; reasons: string[]; competingSeriesIds: string[]; competingPaymentCount: number } }
export type Review = { id: string; transactionId: string; occurrenceDate: string; action: string; snapshot: Snapshot; currentStatus: string; currentTransaction: Snapshot | null; reviewedByUserId: string; reviewedAt: string }

export function money(amount: number, currency: string) {
  return exactAmount(amount, currency)
}

export function label(value: string) {
  if (value === 'due') { return 'payment window open' }
  if (value === 'none-in-range') { return 'no outstanding dates in this range' }
  return value.replaceAll('-', ' ')
}

export function recurringError(error: Error) {
  if (isAxiosError(error)) {
    const data: unknown = error.response?.data
    if (typeof data === 'string') { return data }
    if (data && typeof data === 'object' && 'detail' in data && typeof data.detail === 'string') { return data.detail }
  }
  return 'Unable to complete this request. Refresh and try again.'
}

export function getSeries(range: Range) {
  return httpClient<SeriesList>({ url: recurringUrl, params: range })
}
