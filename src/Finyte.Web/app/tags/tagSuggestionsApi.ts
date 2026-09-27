import { httpClient } from '../api/httpClient'

export type TagCoverage = { currency: string; taggedMinorUnits: number; totalMinorUnits: number }
export type MerchantSuggestion = { ruleMerchantName: string; examples: string[]; transactionCount: number; spendMinorUnits: number; currency: string; reason: string | null }
export type TagSuggestionGroup = { tagName: string; tagId: string | null; color: string; merchants: MerchantSuggestion[] }
export type TagSuggestions = { from: string; to: string; coverage: TagCoverage[]; groups: TagSuggestionGroup[]; needsTag: MerchantSuggestion[] }
export type AcceptItem = { merchantName: string; tagId?: string; tagName?: string }

export const starterTagNames = ['Groceries', 'Eating out', 'Transport', 'Fuel', 'Subscriptions', 'Bills and utilities', 'Health', 'Shopping', 'Home', 'Entertainment', 'Travel', 'Transfers']

export function getTagSuggestions() {
  return httpClient<TagSuggestions>({ url: '/api/tag-suggestions' })
}

export function acceptTagSuggestions(items: AcceptItem[]) {
  return httpClient<{ createdTags: number; createdRules: number }>({ method: 'POST', url: '/api/tag-suggestions/accept', data: { items } })
}
