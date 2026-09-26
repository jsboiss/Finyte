import { Help } from '../../shared/Help'
import { compactCurrency, exactCurrency } from '../../shared/formatters'
import { categoryTransactionLink } from '../transactionLinks'
import type { OverviewResponse } from '../types'

export function SpendByCategoryChart({ categories, currencyCode, overview }: {
  categories: NonNullable<OverviewResponse['monthlySpendByCategory']>
  currencyCode: string
  overview: OverviewResponse
}) {
  const total = categories.reduce((x, y) => x + y.amountMinorUnits, 0)
  const largest = categories[0]?.amountMinorUnits ?? 0

  if (categories.length === 0) {
    return <p className="category-empty">No spending recorded this month.</p>
  }

  return (
    <div className="category-chart">
      <ol className="category-bars">
        {categories.map(x => (
          <li key={x.name}>
            <a href={categoryTransactionLink(overview, x.name)}>
              <span className="category-bar-label">
                <span>{x.name}</span>
                <strong title={exactCurrency(x.amountMinorUnits, currencyCode)}>{compactCurrency(x.amountMinorUnits, currencyCode)}</strong>
              </span>
              <span aria-hidden="true" className="category-bar-track">
                <span className="category-bar-fill" style={{ width: `${largest > 0 ? (x.amountMinorUnits / largest) * 100 : 0}%` }} />
              </span>
              <small>{x.percentage.toFixed(1)}% of tracked spending</small>
            </a>
          </li>
        ))}
      </ol>
      <p className="category-total">
        {compactCurrency(total, currencyCode)} across {categories.length} {categories.length === 1 ? 'category' : 'categories'}.
        <Help title="About categories">
          <p>Every payment belongs to exactly one category, so these add up to tracked spending. Tags can be split across a payment; categories cannot.</p>
          <p>A category comes from your correction first, then the bank, then a merchant rule. Anything with none is shown as Uncategorised.</p>
        </Help>
      </p>
    </div>
  )
}
