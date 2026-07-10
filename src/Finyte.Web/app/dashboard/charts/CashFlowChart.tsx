import { memo } from 'react'
import { currency, signedCurrency } from '../../shared/formatters'
import type { CashFlowPoint, DashboardChart } from '../types'

export const CashFlowChart = memo(function CashFlowChart({
  chart,
  currencyCode,
  points,
}: {
  chart: DashboardChart
  currencyCode: string
  points: CashFlowPoint[]
}) {
  if (chart === 'summary') {
    return <CashFlowSummary points={points} currencyCode={currencyCode} />
  }

  if (chart === 'weekly-bars') {
    return <CashFlowWeeklyBars points={points} currencyCode={currencyCode} />
  }

  return <CashFlowDailyBars points={points} currencyCode={currencyCode} />
})

const CashFlowSummary = memo(function CashFlowSummary({ points, currencyCode }: { points: CashFlowPoint[]; currencyCode: string }) {
  const income = points.reduce((x, y) => x + y.incomeMinorUnits, 0)
  const expenses = points.reduce((x, y) => x + y.expenseMinorUnits, 0)
  const net = income - expenses
  const total = Math.max(income + expenses, 1)
  const incomePercent = (income / total) * 100
  const expensePercent = (expenses / total) * 100
  const bestIncomeDay = [...points].sort((x, y) => y.incomeMinorUnits - x.incomeMinorUnits)[0]
  const highestExpenseDay = [...points].sort((x, y) => y.expenseMinorUnits - x.expenseMinorUnits)[0]

  return (
    <div className="cash-flow-summary">
      <div className="cash-flow-summary-net">
        <span>Net cash flow</span>
        <strong className={net >= 0 ? 'amount-positive' : 'amount-negative'}>{signedCurrency(net, currencyCode)}</strong>
      </div>
      <div className="summary-race-track" aria-label="Income vs expenses month to date">
        <div className="race-income" style={{ width: `${incomePercent}%` }} />
        <div className="race-expense" style={{ width: `${expensePercent}%` }} />
      </div>
      <div className="cash-flow-summary-grid">
        <div>
          <span>Income</span>
          <strong>{currency(income, currencyCode)}</strong>
        </div>
        <div>
          <span>Expenses</span>
          <strong>{currency(expenses, currencyCode)}</strong>
        </div>
      </div>
      <div className="cash-flow-highlights">
        <div>
          <span>Best income day</span>
          <strong>{bestIncomeDay && bestIncomeDay.incomeMinorUnits > 0 ? `${bestIncomeDay.tooltip} ${currency(bestIncomeDay.incomeMinorUnits, currencyCode)}` : 'No income yet'}</strong>
        </div>
        <div>
          <span>Highest spend day</span>
          <strong>{highestExpenseDay && highestExpenseDay.expenseMinorUnits > 0 ? `${highestExpenseDay.tooltip} ${currency(highestExpenseDay.expenseMinorUnits, currencyCode)}` : 'No spend yet'}</strong>
        </div>
      </div>
    </div>
  )
})

const CashFlowDailyBars = memo(function CashFlowDailyBars({ points, currencyCode }: { points: CashFlowPoint[]; currencyCode: string }) {
  const max = Math.max(...points.map(x => x.incomeMinorUnits + x.expenseMinorUnits), 1)

  return (
    <div className="daily-chart">
      <div className="daily-bars" style={{ gridTemplateColumns: `repeat(${Math.max(points.length, 1)}, minmax(0, 1fr))` }}>
        {points.map((x, index) => {
          const incomeHeight = Math.max((x.incomeMinorUnits / max) * 100, x.incomeMinorUnits > 0 ? 3 : 0)
          const expenseHeight = Math.max((x.expenseMinorUnits / max) * 100, x.expenseMinorUnits > 0 ? 3 : 0)
          return (
            <div className="daily-bar" key={x.id}>
              <div className="daily-tooltip" style={{ left: getTooltipPosition(index, points.length) }}>
                <strong>{x.tooltip}</strong>
                <span>Income {currency(x.incomeMinorUnits, currencyCode)}</span>
                <span>Expense {currency(x.expenseMinorUnits, currencyCode)}</span>
              </div>
              <div className="daily-bar-stack">
                {x.incomeMinorUnits > 0 && <div className="daily-income" style={{ height: `${incomeHeight}%` }} />}
                {x.expenseMinorUnits > 0 && <div className="daily-expense" style={{ height: `${expenseHeight}%` }} />}
              </div>
              <span>{x.label}</span>
            </div>
          )
        })}
      </div>
      <CashFlowLegend />
    </div>
  )
})

const CashFlowWeeklyBars = memo(function CashFlowWeeklyBars({ points, currencyCode }: { points: CashFlowPoint[]; currencyCode: string }) {
  const max = Math.max(...points.map(x => x.incomeMinorUnits + x.expenseMinorUnits), 1)

  return (
    <div className="weekly-chart">
      <div className="weekly-bars" style={{ gridTemplateColumns: `repeat(${Math.max(points.length, 1)}, minmax(0, 1fr))` }}>
        {points.map((x, index) => {
          const incomeHeight = Math.max((x.incomeMinorUnits / max) * 100, x.incomeMinorUnits > 0 ? 4 : 0)
          const expenseHeight = Math.max((x.expenseMinorUnits / max) * 100, x.expenseMinorUnits > 0 ? 4 : 0)
          const net = x.incomeMinorUnits - x.expenseMinorUnits
          return (
            <div className="weekly-bar" key={x.id}>
              <div className="daily-tooltip" style={{ left: getTooltipPosition(index, points.length) }}>
                <strong>{x.tooltip}</strong>
                <span>Income {currency(x.incomeMinorUnits, currencyCode)}</span>
                <span>Expense {currency(x.expenseMinorUnits, currencyCode)}</span>
                <span>Net {signedCurrency(net, currencyCode)}</span>
              </div>
              <div className="weekly-bar-stack">
                {x.incomeMinorUnits > 0 && <div className="daily-income" style={{ height: `${incomeHeight}%` }} />}
                {x.expenseMinorUnits > 0 && <div className="daily-expense" style={{ height: `${expenseHeight}%` }} />}
              </div>
              <div className="weekly-bar-footer">
                <span>{x.label}</span>
                <strong className={net >= 0 ? 'amount-positive' : 'amount-negative'}>{signedCurrency(net, currencyCode)}</strong>
              </div>
            </div>
          )
        })}
      </div>
      <CashFlowLegend />
    </div>
  )
})

function CashFlowLegend() {
  return (
    <div className="chart-legend">
      <span><i className="legend-income" />Income</span>
      <span><i className="legend-expense" />Expense</span>
    </div>
  )
}

function getTooltipPosition(index: number, pointCount: number) {
  const position = ((index + 0.5) / Math.max(pointCount, 1)) * 100
  return `clamp(90px, ${position}%, calc(100% - 90px))`
}
