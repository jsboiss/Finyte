import { Help } from '../../shared/Help'
import { currency } from '../../shared/formatters'
import type { OverviewResponse } from '../types'

export function SpendByTagChart({ tags, currencyCode }: { tags: OverviewResponse['monthlySpendByTag']; currencyCode: string }) {
  const total = tags.reduce((x, y) => x + y.amountMinorUnits, 0)
  const primary = tags[0]
  const background = total > 0
    ? `conic-gradient(${getPieStops(tags).join(', ')})`
    : 'conic-gradient(#36393d 0 100%)'

  return (
    <div className="tag-chart">
      <div className="pie-chart" style={{ background }}>
        <div>
          <span>Total</span>
          <strong>{currency(total, currencyCode)}</strong>
        </div>
      </div>
      <div className="tag-list">
        {tags.map(x => (
          <div className="tag-bucket-link" key={x.tagId ?? 'untagged'}>
            <span><i style={{ backgroundColor: x.color }} />{x.name}</span>
            <strong>{currency(x.amountMinorUnits, currencyCode)}</strong>
          </div>
        ))}
        <p>{primary && <>{primary.name} is currently {primary.percentage.toFixed(1)}% of tracked monthly spend. </>}<Help title="About tag amounts"><p>Amounts on transactions with multiple tags are split between those tags. The transaction list shows each payment’s full amount.</p></Help></p>
      </div>
    </div>
  )
}

function getPieStops(tags: OverviewResponse['monthlySpendByTag']) {
  let start = 0
  return tags.map(x => {
    const end = start + x.percentage
    const stop = `${x.color} ${start}% ${end}%`
    start = end
    return stop
  })
}
