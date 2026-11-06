import { useRef, useState } from 'react'
import { compactCurrency, exactAmount } from '../shared/formatters'
import { monthLabel, type HomeLoanMonth, type HomeLoanReport } from './homeLoanApi'

type Segment = { key: string; label: string; value: number; className: string }
type Period = { key: string; label: string; month: HomeLoanMonth }

function moneyIn(data: HomeLoanReport, month: HomeLoanMonth): Segment[] {
  return [
    { key: 'first', label: data.setup.firstApplicant, value: month.firstApplicantIncome, className: 'is-in-1' },
    ...(data.setup.secondApplicant ? [{ key: 'second', label: data.setup.secondApplicant, value: month.secondApplicantIncome, className: 'is-in-2' }] : []),
    { key: 'other', label: 'Other income', value: month.otherIncome, className: 'is-in-3' },
    { key: 'unlabelled', label: 'Not yet labelled', value: month.unlabelledCredits, className: 'is-unlabelled' },
  ]
}

function moneyOut(month: HomeLoanMonth): Segment[] {
  const saved = Math.max(month.saved, 0)
  return [
    { key: 'living', label: 'Living expenses', value: month.livingExpenses, className: 'is-out-1' },
    { key: 'commitments', label: 'Commitments', value: month.commitments, className: 'is-out-2' },
    { key: 'saved', label: 'Saved', value: saved, className: 'is-out-3' },
    { key: 'unspent', label: 'Unspent, not saved', value: Math.max(month.surplus - saved, 0), className: 'is-unspent' },
  ]
}

const sum = (segments: Segment[]) => segments.reduce((total, x) => total + Math.max(x.value, 0), 0)

function niceMax(value: number) {
  const power = 10 ** Math.floor(Math.log10(value))
  return ([1, 2, 2.5, 5, 10].find(x => x * power >= value) ?? 10) * power
}

export function MoneyFlowChart({ data }: { data: HomeLoanReport }) {
  const periods: Period[] = [...data.monthlyTotals.map(x => ({ key: x.month, label: monthLabel(x.month), month: x })), { key: 'average', label: 'Average', month: data.average }]
  const [pinned, setPinned] = useState('average')
  const [hovered, setHovered] = useState<string | null>(null)
  const buttons = useRef<(HTMLButtonElement | null)[]>([])
  const current = periods.find(x => x.key === (hovered ?? pinned)) ?? periods[periods.length - 1]
  const pinnedIndex = Math.max(0, periods.findIndex(x => x.key === pinned))
  const money = (value: number) => exactAmount(value, data.currency)
  const scale = niceMax(Math.max(1, ...periods.flatMap(x => [sum(moneyIn(data, x.month)), sum(moneyOut(x.month))])) * 1.05)
  const legendIn = moneyIn(data, current.month)
  const legendOut = moneyOut(current.month)
  const income = current.month.firstApplicantIncome + current.month.secondApplicantIncome + current.month.otherIncome
  const overspent = current.month.livingExpenses + current.month.commitments - income
  const move = (index: number) => {
    const next = (index + periods.length) % periods.length
    setPinned(periods[next].key)
    buttons.current[next]?.focus()
  }

  return <section className="panel home-loan-section home-loan-flow" aria-labelledby="home-loan-flow-title">
    <div className="home-loan-flow-heading">
      <h2 id="home-loan-flow-title">How your money moves</h2>
      <p>For each month, the In column is money coming in, stacked by person, and the Out column is where it went. Select a month to see its figures.</p>
    </div>
    <div className="home-loan-chart">
      <div className="home-loan-chart-axis" aria-hidden="true">{[scale, scale / 2, 0].map(x => <span key={x}>{compactCurrency(Math.round(x * 100), data.currency)}</span>)}</div>
      <div className="home-loan-chart-plot" role="group" aria-label="Months" onMouseLeave={() => setHovered(null)}>
        <div className="home-loan-chart-grid" aria-hidden="true"><span /><span /><span /></div>
        {periods.map((period, index) => <button key={period.key} ref={element => { buttons.current[index] = element }} type="button"
          tabIndex={index === pinnedIndex ? 0 : -1} aria-pressed={period.key === pinned}
          className={`home-loan-chart-month${period.key === pinned ? ' is-selected' : ''}${period.key === 'average' ? ' is-average' : ''}`}
          onClick={() => setPinned(period.key)} onMouseEnter={() => setHovered(period.key)}
          onKeyDown={event => {
            const target = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? index + 1
              : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? index - 1
              : event.key === 'Home' ? 0 : event.key === 'End' ? periods.length - 1 : null
            if (target !== null) {
              event.preventDefault()
              move(target)
            }
          }}
          aria-label={`${period.label}: money in ${money(sum(moneyIn(data, period.month)))}, money out ${money(sum(moneyOut(period.month)))}`}>
          <span className="home-loan-chart-columns" aria-hidden="true">
            <Column segments={moneyIn(data, period.month)} scale={scale} />
            <Column segments={moneyOut(period.month)} scale={scale} />
          </span>
          <span className="home-loan-chart-captions" aria-hidden="true"><span>In</span><span>Out</span></span>
          <span className="home-loan-chart-label" aria-hidden="true">{period.label}</span>
        </button>)}
      </div>
    </div>
    <div className="home-loan-chart-detail">
      <h3>{current.key === 'average' ? `Average month (${data.months} months)` : current.label}</h3>
      <div className="home-loan-chart-legends">
        <Legend title="Money in (In column)" segments={legendIn} total={sum(legendIn)} money={money} />
        <Legend title="Where it went (Out column)" segments={legendOut} total={sum(legendOut)} money={money} />
      </div>
      {overspent > 0 && <p className="home-loan-flow-note">Spending and commitments were {money(overspent)} more than labelled income{current.month.unlabelledCredits > 0 ? '. Label the remaining credits to check this.' : '.'}</p>}
      {current.month.saved < 0 && <p className="home-loan-flow-note">{money(-current.month.saved)} more came out of savings than went in.</p>}
    </div>
  </section>
}

function Column({ segments, scale }: { segments: Segment[]; scale: number }) {
  return <span className="home-loan-chart-column">
    {segments.filter(x => x.value > 0).reverse().map(x => <span key={x.key} className={`home-loan-chart-segment ${x.className}`} style={{ height: `${(x.value / scale) * 100}%` }} />)}
  </span>
}

function Legend({ title, segments, total, money }: { title: string; segments: Segment[]; total: number; money: (value: number) => string }) {
  return <div>
    <span className="home-loan-chart-legend-title">{title} <strong>{money(total)}</strong></span>
    <ul className="home-loan-flow-legend">
      {segments.filter(x => x.value > 0 || x.key !== 'unlabelled').map(x => <li key={x.key}>
        <span className={`home-loan-flow-key ${x.className}`} />{x.label} <strong>{money(x.value)}</strong>
      </li>)}
    </ul>
  </div>
}
