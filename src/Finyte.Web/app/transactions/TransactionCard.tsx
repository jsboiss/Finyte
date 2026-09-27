import { CreditCard } from '../shared/Icons'
import { Link } from '@tanstack/react-router'
import { type CSSProperties, type ReactNode } from 'react'
import { exactCurrency, formatChartDate } from '../shared/formatters'
import type { Transaction } from './types'

type TransactionAccountChipProps = {
  accountId: string
  children: ReactNode
}

type TransactionAllocation = {
  minorUnits: number
  tagName: string
}

type TransactionAmountProps = {
  allocation?: TransactionAllocation
  amountMinorUnits: number
  className?: string
  currencyCode: string
}

type TransactionCardProps = {
  allocation?: TransactionAllocation
  tags: ReactNode
  transaction: Transaction
}

export function TransactionCard({ allocation, tags, transaction }: TransactionCardProps) {
  return (
    <article className="transaction-card" style={{ '--account-color': getAccountColor(transaction.accountId) } as CSSProperties}>
      <div className="transaction-card-account-header">
        <CreditCard aria-hidden="true" />
        <span>{transaction.accountDisplayName}</span>
        <time dateTime={transaction.postedDate}>{formatChartDate(transaction.postedDate)}</time>
      </div>
      <div className="transaction-card-body">
        <div className="transaction-card-heading">
          <div className="transaction-card-description">
            <strong>{transaction.description}</strong>
            {transaction.isInternalTransfer && <Link className="transfer-badge" to="/transactions" search={previous => ({ ...previous, transferView: 'confirmed' })}>Internal transfer</Link>}
            {transaction.merchantName && transaction.merchantName.toLowerCase() !== transaction.description?.toLowerCase() && <span>{transaction.merchantName}</span>}
          </div>
          <TransactionAmount allocation={allocation} amountMinorUnits={transaction.amountMinorUnits} className="transaction-card-amount" currencyCode={transaction.currency} />
        </div>
        <div className="transaction-card-footer">{tags}</div>
      </div>
    </article>
  )
}

export function TransactionAccountChip({ accountId, children }: TransactionAccountChipProps) {
  return (
    <span className="account-chip" style={{ '--account-color': getAccountColor(accountId) } as CSSProperties}>
      {children}
    </span>
  )
}

export function TransactionAmount({ allocation, amountMinorUnits, className, currencyCode }: TransactionAmountProps) {
  const amountClassName = amountMinorUnits < 0 ? 'amount-negative' : 'amount-positive'
  const amount = (
    <strong className={className ? `${amountClassName} ${className}` : amountClassName}>
      {exactCurrency(amountMinorUnits, currencyCode)}
    </strong>
  )

  if (!allocation) {
    return amount
  }

  return (
    <span className="transaction-amount-allocated">
      {amount}
      <small>{exactCurrency(allocation.minorUnits, currencyCode)} counted towards {allocation.tagName}</small>
    </span>
  )
}

function getAccountColor(accountId: string) {
  const colors = ['#f5d45f', '#59cbc2', '#aa91ef', '#63c7e5', '#74d58b', '#f59a55', '#6aa3ff', '#f08fc0']
  return colors[getStableIndex(accountId, colors.length)]
}

function getStableIndex(value: string, length: number) {
  let hash = 0

  for (const character of value) {
    hash = (hash * 31 + character.charCodeAt(0)) % length
  }

  return hash
}
