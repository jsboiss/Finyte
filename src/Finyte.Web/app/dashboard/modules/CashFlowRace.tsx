import { memo, useEffect, useRef } from 'react'
import { currency, signedCurrency } from '../../shared/formatters'
import type { CashFlowModuleData } from '../types'

export const CashFlowRace = memo(function CashFlowRace({ currencyCode, data, isLoading }: { currencyCode: string; data: CashFlowModuleData; isLoading: boolean }) {
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

  return (
    <div className="cash-flow-race">
      <div className="cash-flow-values">
        <div className="cash-flow-value is-income">
          <span>Income</span>
          <strong>{currency(income, currencyCode)}</strong>
        </div>
        <div className="cash-flow-value is-net">
          <span>Net</span>
          <strong>{signedCurrency(net, currencyCode)}</strong>
        </div>
        <div className="cash-flow-value is-expense">
          <span>Expenses</span>
          <strong>{currency(expenses, currencyCode)}</strong>
        </div>
      </div>
      <div className="race-track" aria-label="Income vs expenses">
        <div className="race-income" style={{ width: `${incomePercent}%` }} />
        <div className="race-expense" style={{ width: `${expensePercent}%` }} />
      </div>
    </div>
  )
})
