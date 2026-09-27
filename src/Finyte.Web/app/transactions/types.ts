export type TransactionTag = {
  id: string
  name: string
  color: string
  source?: 'manual' | 'merchant-rule' | 'system' | 'legacy' | null
  merchantRuleId?: string | null
  merchantRuleName?: string | null
}

export type Transaction = {
  id: string
  accountId: string
  accountDisplayName: string
  postedDate: string
  description: string
  merchantName: string | null
  category: string
  amountMinorUnits: number
  currency: string
  tags: TransactionTag[]
  automaticTagExclusions?: string[]
  isInternalTransfer?: boolean
  internalTransferAccountId?: string | null
  internalTransferAccountName?: string | null
  internalTransferSource?: string | null
}

export type TransactionPage = {
  items: Transaction[]
  page: number
  pageSize: number
  totalCount: number
}
