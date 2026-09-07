export type TransactionTag = {
  id: string
  name: string
  color: string
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
  isInternalTransfer?: boolean
}

export type TransactionPage = {
  items: Transaction[]
  page: number
  pageSize: number
  totalCount: number
}
