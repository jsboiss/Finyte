import { memo, useEffect, useRef } from 'react'
import { compactCurrency, signedCompactCurrency } from '../../shared/formatters'
import { transactionLink } from '../transactionLinks'
import type { CashFlowModuleData, OverviewResponse } from '../types'

export const CashFlowRace = memo(function CashFlowRace({ currencyCode, data, isLoading, overview }: { overview: OverviewResponse; currencyCode: string; data: CashFlowModuleData; isLoading: boolean }) {
  const settledDataRef = useRef(data)

  useEffect(() => {
    if (!isLoading) {
      settledDataRef.current = data
    }
  }, [data, isLoading])

  const visibleData = isLoading ? settledDataRef.current : data
  const income = visibleData.points.reduce((x, y) => x + y.incomeMinorUnits, 0)
  const expenses = visibleData.points.reduce((x, y) => x + y.expenseMinorUnits, 0)
  const net = income - expenses
  const total = Math.max(income + expenses, 1)
  const incomePercent = (income / total) * 100
  const expensePercent = (expenses / total) * 100

  const range = { from: visibleData.points[0]?.from ?? overview.monthKey + '-01', to: visibleData.points.at(-1)?.to ?? overview.monthKey + '-01' }
  return (
    <div className="cash-flow-race">
      <div className="cash-flow-values">
        <a href={transactionLink(overview, 'credit', range)} className="cash-flow-value is-income">
          <span>Income</span>
          <strong>{compactCurrency(income, currencyCode)}</strong>
        </a>
        <a href={transactionLink(overview, 'all', range)} className="cash-flow-value is-net">
          <span>Net</span>
          <strong>{signedCompactCurrency(net, currencyCode)}</strong>
        </a>
        <a href={transactionLink(overview, 'debit', range)} className="cash-flow-value is-expense">
          <span>Expenses</span>
          <strong>{compactCurrency(expenses, currencyCode)}</strong>
        </a>
      </div>
      <div className="race-track" aria-label="Income vs expenses">
        <div className="race-income" style={{ width: `${incomePercent}%` }} />
        <div className="race-expense" style={{ width: `${expensePercent}%` }} />
      </div>
    </div>
  )
})
