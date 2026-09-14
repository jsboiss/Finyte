import { Help } from '../../shared/Help'
import { currency } from '../../shared/formatters'
import type { OverviewResponse } from '../types'
import { transactionLink } from '../transactionLinks'

export function SpendByTagChart({ tags, currencyCode, overview }: { tags: OverviewResponse['monthlySpendByTag']; currencyCode: string; overview: OverviewResponse }) {
  const total = tags.reduce((x, y) => x + y.amountMinorUnits, 0)
  const primary = tags[0]
  const slices = tags.filter(x => x.amountMinorUnits > 0).reduce<{ tag: typeof tags[number]; start: number; end: number }[]>((x, y) => {
    const start = x.at(-1)?.end ?? 0
    return [...x, { tag: y, start, end: start + y.amountMinorUnits / total * Math.PI * 2 }]
  }, [])

  return (
    <div className="tag-chart">
      <div className="pie-chart">
        <svg viewBox="0 0 210 210" role="group" aria-label="Spending by tag: select a slice to view transactions">
          {total <= 0 && <circle cx="105" cy="105" r="83.5" fill="none" stroke="#36393d" strokeWidth="43" />}
          {slices.map(x => (
            <a key={x.tag.tagId ?? 'untagged'} href={transactionLink(overview, 'debit', undefined, x.tag.tagId)} aria-label={`View ${x.tag.name} transactions: ${currency(x.tag.amountMinorUnits, currencyCode)}`}>
              <title>{`${x.tag.name}: ${currency(x.tag.amountMinorUnits, currencyCode)} — view transactions`}</title>
              <path d={slicePath(x.start, x.end)} fill={x.tag.color} />
            </a>
          ))}
        </svg>
        <div>
          <span>Total</span>
          <strong>{currency(total, currencyCode)}</strong>
        </div>
      </div>
      <div className="tag-list">
        {tags.map(x => (
          <a className="tag-bucket-link" href={transactionLink(overview, 'debit', undefined, x.tagId)} key={x.tagId ?? 'untagged'}>
            <span><i style={{ backgroundColor: x.color }} />{x.name}</span>
            <strong>{currency(x.amountMinorUnits, currencyCode)}</strong>
          </a>
        ))}
        <p>{primary && <>{primary.name} is currently {primary.percentage.toFixed(1)}% of tracked monthly spend. </>}<Help title="About tag amounts"><p>Amounts on transactions with multiple tags are split between those tags. The transaction list shows each payment’s full amount.</p></Help></p>
      </div>
    </div>
  )
}

function slicePath(start: number, end: number) {
  const point = (radius: number, angle: number) => `${105 + radius * Math.sin(angle)} ${105 - radius * Math.cos(angle)}`
  const middle = (start + end) / 2
  // Two arcs per edge also handle the single-tag, full-circle case.
  return `M ${point(105, start)} A 105 105 0 0 1 ${point(105, middle)} A 105 105 0 0 1 ${point(105, end)} L ${point(62, end)} A 62 62 0 0 0 ${point(62, middle)} A 62 62 0 0 0 ${point(62, start)} Z`
}
