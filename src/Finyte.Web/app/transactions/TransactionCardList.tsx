import type { ReactNode } from 'react'
import { TransactionCard } from './TransactionCard'
import type { Transaction } from './types'

type TransactionCardListProps = {
  emptyMessage: string
  isLoading: boolean
  renderTags: (transaction: Transaction) => ReactNode
  transactions: Transaction[]
}

export function TransactionCardList({ emptyMessage, isLoading, renderTags, transactions }: TransactionCardListProps) {
  return (
    <section className="transaction-mobile-list" aria-label="Transactions">
      <div className={isLoading ? 'table-progress is-visible' : 'table-progress'} />
      {transactions.map(transaction => (
        <TransactionCard key={transaction.id} tags={renderTags(transaction)} transaction={transaction} />
      ))}
      {transactions.length === 0 && <p className="transaction-mobile-empty">{emptyMessage}</p>}
    </section>
  )
}
