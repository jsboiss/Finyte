import { exactCurrency, signedCurrency } from '../shared/formatters'
import type { TransactionTotals } from './types'

const transferNotes: Record<string, string> = {
  exclude: 'Matched internal transfers are excluded.',
  include: 'Internal transfers are included.',
  only: 'Only matched internal transfers are counted.',
}

export function TransactionResultTotals({ internalTransfers, isLoading, isError, totals }: {
  internalTransfers: string
  isLoading: boolean
  isError: boolean
  totals: TransactionTotals[] | undefined
}) {
  if (isError) {
    return null
  }
  if (isLoading || !totals) {
    return <section aria-busy="true" className="result-totals"><p role="status">Totalling matching transactions…</p></section>
  }
  if (totals.length === 0) {
    return null
  }

  return (
    <section aria-label="Totals for all matching transactions" className="result-totals">
      {totals.map(x => (
        <div className="result-totals-currency" key={x.currency}>
          <h3>{x.currency}</h3>
          <dl>
            <div><dt>Matching</dt><dd>{x.transactionCount}</dd></div>
            <div><dt>Money in</dt><dd className="amount-in">{exactCurrency(x.moneyInMinorUnits, x.currency)}</dd></div>
            <div><dt>Money out</dt><dd className="amount-out">{exactCurrency(x.moneyOutMinorUnits, x.currency)}</dd></div>
            <div><dt>Net</dt><dd>{signedCurrency(x.netMinorUnits, x.currency)}</dd></div>
          </dl>
        </div>
      ))}
      <p className="result-totals-note">
        {totals.length > 1 && 'Currencies are totalled separately and never converted. '}
        {transferNotes[internalTransfers] ?? transferNotes.include}
      </p>
    </section>
  )
}
