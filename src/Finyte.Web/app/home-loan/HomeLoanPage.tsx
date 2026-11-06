import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { Drawer } from '../shared/Drawer'
import { exactAmount } from '../shared/formatters'
import { Help } from '../shared/Help'
import {
  dayLabel, downloadCsv, getHomeLoan, matchKey, monthLabel, pendingCategories, saveCategory, saveIncomeSource, savePaymentCategory,
  type HomeLoanPayer, type HomeLoanPayment, type HomeLoanReport, type HomeLoanTag, type LenderCategory,
} from './homeLoanApi'
import { HomeLoanSetup } from './HomeLoanSetup'
import { MoneyFlowChart } from './MoneyFlowChart'
import './homeLoan.css'

const groupLabels = { living: 'Living expense', commitment: 'Commitment', savings: 'Saved' } as const

export function HomeLoanPage() {
  const [months, setMonths] = useState(3)
  const [end, setEnd] = useState('')
  const [editing, setEditing] = useState(false)
  const [message, setMessage] = useState('')
  const queryClient = useQueryClient()
  const report = useQuery({ queryKey: ['home-loan', months, end], queryFn: () => getHomeLoan(months, end), placeholderData: keepPreviousData })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['home-loan'] })
  const download = useMutation({ mutationFn: ({ file, data }: { file: 'summary' | 'transactions'; data: HomeLoanReport }) => downloadCsv(file, data) })
  const data = report.data

  return <section className="page home-loan-page">
    <header className="page-header">
      <div className="page-title"><h1>Home loan</h1><Help title="About the home loan summary"><p>This page turns your transactions into the figures a lender or mortgage broker asks about: income for each applicant, living expenses in the categories lenders use, commitments like rent and loans, and how much you saved. It's a preparation aid. Lenders still ask for your official bank statements, usually at least 3 months for every account, or 6 months for casual or contract income.</p></Help></div>
      {data && <div className="home-loan-actions">
        <button type="button" disabled={download.isPending} onClick={() => download.mutate({ file: 'summary', data })}>Download summary (CSV)</button>
        <button type="button" className="home-loan-secondary" disabled={download.isPending} onClick={() => download.mutate({ file: 'transactions', data })}>Download all transactions (CSV)</button>
      </div>}
    </header>
    {download.isError && <p role="alert">The download didn't work. Try again.</p>}
    {message && <p role="status">{message}</p>}
    {report.isLoading && <p>Working out your summary…</p>}
    {report.isError && <p role="alert">The summary could not be loaded. <button type="button" onClick={() => void report.refetch()}>Retry</button></p>}
    {data && <>
      <div className="home-loan-toolbar panel">
        <label>Period<select value={months} onChange={event => setMonths(Number(event.target.value))}>
          {[3, 6, 12].map(x => <option key={x} value={x}>{x} months</option>)}
        </select></label>
        <label>Ending<select value={end || data.toMonth} onChange={event => setEnd(event.target.value)}>
          {[...data.availableMonths].reverse().map(x => <option key={x} value={x}>{monthLabel(x)}</option>)}
        </select></label>
        <p>{monthLabel(data.fromMonth)} to {monthLabel(data.toMonth)} · {data.setup.secondApplicant ? `${data.setup.firstApplicant} and ${data.setup.secondApplicant}` : data.setup.firstApplicant}
          {report.isFetching && <span className="home-loan-refreshing"> · Updating…</span>}</p>
        <button type="button" className="home-loan-secondary" onClick={() => { setEditing(true); setMessage('') }}>Edit setup</button>
      </div>
      {!data.setup.saved && <div className="home-loan-callout"><p>Start by checking the setup: add both applicants' names, and mark which accounts are savings.</p><button type="button" onClick={() => setEditing(true)}>Check setup</button></div>}
      {data.gaps.length > 0 && <div className="home-loan-callout home-loan-warning"><strong>Some months have no transactions.</strong> Import those statements if the account was open, so the figures are complete.
        <ul>{data.gaps.map(x => <li key={x.accountId}>{x.accountName}: {x.missingMonths.map(monthLabel).join(', ')}</li>)}</ul>
        <Link to="/imports">Import statements</Link></div>}
      {editing && <Drawer title="Home loan setup" onClose={() => setEditing(false)}>
        <HomeLoanSetup key={data.setup.version} report={data} onCancel={() => setEditing(false)}
          onSaved={async () => { setEditing(false); setMessage('Setup saved.'); await refresh() }} />
      </Drawer>}
      <ToDo data={data} />
      <Summary data={data} />
      <MoneyFlowChart data={data} />
      <MonthlyTable data={data} />
      <IncomeSection data={data} onChanged={refresh} />
      <ExpenseSection data={data} onChanged={refresh} />
      <FlagSection data={data} />
    </>}
  </section>
}

function Summary({ data }: { data: HomeLoanReport }) {
  const money = (value: number) => exactAmount(value, data.currency)
  const average = data.average
  const beyondLeftOver = average.saved - Math.max(average.surplus, 0)
  return <>
    <section className="home-loan-metrics" aria-label="Monthly averages">
      <article className="panel"><span>Income: {data.setup.firstApplicant}</span><strong>{money(average.firstApplicantIncome)}</strong><small>a month</small></article>
      {data.setup.secondApplicant && <article className="panel"><span>Income: {data.setup.secondApplicant}</span><strong>{money(average.secondApplicantIncome)}</strong><small>a month</small></article>}
      <article className="panel"><span>Living expenses</span><strong>{money(average.livingExpenses)}</strong><small>a month</small></article>
      <article className="panel"><span>Commitments</span><strong>{money(average.commitments)}</strong><small>rent, loans, cards, buy now pay later</small></article>
      <article className="panel"><span>Saved</span><strong>{money(average.saved)}</strong><small>a month · {money(data.total.saved)} in total</small></article>
      <article className="panel"><span>Left over</span><strong className={average.surplus < 0 ? 'home-loan-negative' : ''}>{money(average.surplus)}</strong><small>a month, after expenses and commitments</small></article>
    </section>
    {beyondLeftOver >= 1 && <p className="home-loan-callout">You saved {money(beyondLeftOver)} a month more than was left over, so it came from money you already had{average.unlabelledCredits > 0 ? ' or from money in that isn’t labelled yet' : ''}. A broker may ask where it came from.</p>}
  </>
}

function ToDo({ data }: { data: HomeLoanReport }) {
  const money = (value: number) => exactAmount(value, data.currency)
  const unlabelledCredits = data.payers.filter(x => x.kind === null)
  const unmapped = data.tags.filter(x => x.category === null)
  const review = data.review.filter(x => x.ownCategory === null)
  const spend = data.categories.reduce((sum, x) => sum + x.total, 0)
  const pending = data.categories.filter(x => pendingCategories.includes(x.key)).reduce((sum, x) => sum + x.total, 0)
  const percent = spend > 0 ? Math.round(((spend - pending) / spend) * 100) : 100
  if (unlabelledCredits.length === 0 && pending === 0) {
    return <div className="home-loan-callout home-loan-done"><h2>Everything is labelled</h2><p>Every payment in this period has a label and a lender category. Download the summary when you're ready.</p></div>
  }
  return <section className="home-loan-callout" aria-labelledby="home-loan-todo"><h2 id="home-loan-todo">Still to sort</h2>
    <div className="home-loan-progress"><div role="progressbar" aria-label="Spending in a lender category" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}><span style={{ width: `${percent}%` }} /></div>
      <span>{percent}% of spending is in a lender category</span></div>
    <ul>
      {unlabelledCredits.length > 0 && <li><a href="#home-loan-income">{unlabelledCredits.length} {unlabelledCredits.length === 1 ? 'payer' : 'payers'} of money in</a> · {money(unlabelledCredits.reduce((sum, x) => sum + x.total, 0))}</li>}
      {data.untagged.count > 0 && <li><a href="#home-loan-untagged">{data.untagged.count} {data.untagged.count === 1 ? 'payment' : 'payments'} without a tag</a> · {money(data.untagged.total)}</li>}
      {unmapped.length > 0 && <li><a href="#home-loan-tags">{unmapped.length} {unmapped.length === 1 ? 'tag needs' : 'tags need'} a line on the lender's form</a></li>}
      {review.length > 0 && <li><a href="#home-loan-review">{review.length} {review.length === 1 ? 'payment' : 'payments'} to check</a>, whose tags point to different categories</li>}
    </ul>
  </section>
}

function MonthlyTable({ data }: { data: HomeLoanReport }) {
  const money = (value: number) => exactAmount(value, data.currency)
  const rows = [...data.monthlyTotals.map(x => ({ ...x, label: monthLabel(x.month) })), { ...data.average, label: 'Monthly average' }]
  return <section className="panel home-loan-section"><h2>Month by month</h2>
    <div className="home-loan-table-wrap"><table className="home-loan-table">
      <thead><tr><th scope="col">Month</th><th scope="col">{data.setup.firstApplicant}</th>{data.setup.secondApplicant && <th scope="col">{data.setup.secondApplicant}</th>}<th scope="col">Other income</th><th scope="col">Not yet labelled</th><th scope="col">Living expenses</th><th scope="col">Commitments</th><th scope="col">Saved</th><th scope="col">Left over</th></tr></thead>
      <tbody>{rows.map(x => <tr key={x.label} className={x.label === 'Monthly average' ? 'home-loan-average' : ''}>
        <th scope="row">{x.label}</th><td>{money(x.firstApplicantIncome)}</td>{data.setup.secondApplicant && <td>{money(x.secondApplicantIncome)}</td>}<td>{money(x.otherIncome)}</td><td>{money(x.unlabelledCredits)}</td>
        <td>{money(x.livingExpenses)}</td><td>{money(x.commitments)}</td><td>{money(x.saved)}</td><td className={x.surplus < 0 ? 'home-loan-negative' : ''}>{money(x.surplus)}</td>
      </tr>)}</tbody>
    </table></div>
    {(data.total.refunds > 0 || data.total.gifts > 0) && <p>Not counted as income: {exactAmount(data.total.refunds, data.currency)} in refunds and {exactAmount(data.total.gifts, data.currency)} in gifts. Lenders usually don't treat gifts as genuine savings.</p>}
  </section>
}

function IncomeSection({ data, onChanged }: { data: HomeLoanReport; onChanged: () => Promise<unknown> }) {
  const people = [[1, data.setup.firstApplicant], ...(data.setup.secondApplicant ? [[2, data.setup.secondApplicant]] : [])] as [number, string][]
  const options: [string, string][] = [
    ['', 'Not yet labelled'],
    ...people.map(([id, name]) => [`salary:${id}`, `Salary: ${name}`] as [string, string]),
    ...people.map(([id, name]) => [`other-income:${id}`, `Other income: ${name}`] as [string, string]),
    ['other-income:', 'Other income: shared'],
    ['refund:', 'Refund'], ['gift:', 'Gift'], ['not-income:', 'Not income (e.g. from your own account)'],
  ]
  return <section className="panel home-loan-section" id="home-loan-income"><h2>Money in</h2>
    <p>Payers are listed by their exact statement name. Label one, and choose how much of the name later payments must start with, so a salary that adds a date or reference each time is still covered. Lenders check that salary arrives regularly and matches your payslips.</p>
    {data.payers.length === 0 && <p>No money came in during this period.</p>}
    <ul className="home-loan-list">{data.payers.map(payer => <PayerRow key={`${payer.key}-${payer.matchedBy ?? ''}`} payer={payer} payers={data.payers} options={options} currency={data.currency} onChanged={onChanged} />)}</ul>
  </section>
}

function PayerRow({ payer, payers, options, currency, onChanged }: {
  payer: HomeLoanPayer; payers: HomeLoanPayer[]; options: [string, string][]; currency: string; onChanged: () => Promise<unknown>
}) {
  const saved = payer.kind ? `${payer.kind}:${payer.applicant ?? ''}` : ''
  const [choice, setChoice] = useState(options.some(([value]) => value === saved) ? saved : '')
  const [match, setMatch] = useState(payer.matchedBy ?? payer.key)
  const save = useMutation({ mutationFn: saveIncomeSource, onSuccess: onChanged })
  const key = matchKey(match)
  const covered = key ? payers.filter(x => x.key === key || x.key.startsWith(`${key} `)) : []
  const others = covered.filter(x => x.key !== payer.key)
  const unchanged = payer.kind ? choice === saved && key === payer.matchedBy : choice === ''
  const invalid = choice !== '' && (key.length === 0 || !covered.some(x => x.key === payer.key))
  return <li className={payer.kind ? '' : 'home-loan-needs'}>
    <div><strong>{payer.name}</strong>
      <span>{payer.count} {payer.count === 1 ? 'payment' : 'payments'} in {payer.monthsSeen} {payer.monthsSeen === 1 ? 'month' : 'months'} · {exactAmount(payer.total, currency)}{payer.kind ? '' : ' · needs a label'}</span>
      {payer.matchedBy && payer.matchedBy !== payer.key && <span>Labelled by payments starting with “{payer.matchedBy}”</span>}
      <Payments payments={payer.payments} currency={currency} label={payer.name} /></div>
    <form className="home-loan-apply home-loan-payer-form" onSubmit={event => {
      event.preventDefault()
      const [kind, applicant] = choice.split(':')
      save.mutate(kind
        ? { key, previousKey: payer.matchedBy, name: payer.name, kind, applicant: applicant ? Number(applicant) : null }
        : { key: payer.matchedBy ?? key, previousKey: null, name: payer.name, kind: null, applicant: null })
    }}>
      <select aria-label={`Label for ${payer.name}`} value={choice} onChange={event => setChoice(event.target.value)}>
        {options.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
      </select>
      {choice !== '' && <label className="home-loan-match">Applies to payments starting with
        <input value={match} maxLength={200} onChange={event => setMatch(event.target.value)} /></label>}
      {choice !== '' && <small className="home-loan-hint">{invalid ? 'This payer’s name must start with this text.'
        : others.length > 0 ? `Also covers ${others.length} other ${others.length === 1 ? 'payer' : 'payers'}: ${others.slice(0, 3).map(x => x.name).join(', ')}${others.length > 3 ? '…' : ''}` : 'Covers only this payer.'}</small>}
      <button type="submit" className={unchanged ? 'home-loan-secondary' : ''} disabled={save.isPending || unchanged || invalid}>{save.isPending ? 'Saving…' : 'Save'}</button>
    </form>
    {save.isError && <p role="alert">That label could not be saved. Try again.</p>}
  </li>
}

function ExpenseSection({ data, onChanged }: { data: HomeLoanReport; onChanged: () => Promise<unknown> }) {
  const money = (value: number) => exactAmount(value, data.currency)
  const untagged = data.categories.find(x => x.key === 'untagged')
  const shared = data.tags.reduce((sum, x) => sum + x.total, 0) > data.categories.filter(x => x.key !== 'untagged').reduce((sum, x) => sum + x.total, 0) + 0.005
  return <section className="panel home-loan-section" id="home-loan-expenses"><h2>Money out</h2>
    <p>Your spending by your own tags. Choose which line of the lender's living-expense form each tag counts as, and give a single payment its own line when its tag doesn't fit, like a home purchase under a broad Home tag. Only confirmed transfers between your own accounts are left out.</p>
    {data.categories.length === 0 && <p>No spending in this period.</p>}

    {(data.tags.length > 0 || untagged) && <>
      <h3 id="home-loan-tags">Your spending by tag</h3>
      {shared && <p>A payment with more than one tag appears under each of its tags, so these add up to more than you spent.</p>}
      <ul className="home-loan-list">
        {data.tags.map(x => <TagCategoryRow key={`${x.tagId}-${x.category ?? ''}`} tag={x} categories={data.lenderCategories} currency={data.currency} onChanged={onChanged} />)}
        {untagged && <li className="home-loan-needs" id="home-loan-untagged">
          <div><strong>Not tagged yet</strong>
            <span>{data.untagged.count} {data.untagged.count === 1 ? 'payment' : 'payments'} · {money(untagged.monthlyAverage)} a month · {money(untagged.total)}</span>
            <span>Counted as a living expense until tagged. Tag them as you normally would and they'll appear above under their tag.</span></div>
          <div className="home-loan-actions"><Link to="/tag-suggestions">Tag suggestions</Link><Link to="/transactions">Transactions</Link></div>
        </li>}
      </ul>
    </>}

    {data.review.length > 0 && <>
      <h3 id="home-loan-review">Payments to check ({data.review.length})</h3>
      <p>These payments have tags on different lines of the lender's form, or their own line. Choose where each one counts. Until then it's counted as a living expense.</p>
      <ul className="home-loan-list">{data.review.map(x => <li key={x.transactionId}>
        <div><strong>{x.description}</strong><span>{dayLabel(x.date)} · {x.account} · {money(-x.amount)} · tagged {x.tags.join(', ')}</span></div>
        <PaymentCategory payment={x} categories={data.lenderCategories} label={`${x.description} on ${dayLabel(x.date)}`} onChanged={onChanged} />
      </li>)}</ul>
    </>}

    {data.categories.length > 0 && <>
      <h3>For the lender's form</h3>
      <p>The same spending on the lines of a lender's living-expense form, ready to copy across.</p>
      <div className="home-loan-table-wrap"><table className="home-loan-table">
        <thead><tr><th scope="col">Line on the form</th><th scope="col">Type</th><th scope="col">Per month</th><th scope="col">Total</th></tr></thead>
        <tbody>{data.categories.map(x => <tr key={x.key} className={pendingCategories.includes(x.key) ? 'home-loan-needs-row' : ''}>
          <th scope="row">{x.label}</th><td>{groupLabels[x.group]}</td><td>{money(x.monthlyAverage)}</td><td>{money(x.total)}</td>
        </tr>)}</tbody>
      </table></div>
    </>}
  </section>
}

function CategoryOptions({ categories }: { categories: LenderCategory[] }) {
  return <>{(['living', 'commitment', 'savings'] as const).map(group => <optgroup key={group} label={groupLabels[group]}>
    {categories.filter(y => y.group === group).map(y => <option key={y.key} value={y.key}>{y.label}</option>)}
  </optgroup>)}</>
}

function TagCategoryRow({ tag, categories, currency, onChanged }: { tag: HomeLoanTag; categories: LenderCategory[]; currency: string; onChanged: () => Promise<unknown> }) {
  const [choice, setChoice] = useState(tag.category ?? '')
  const save = useMutation({ mutationFn: saveCategory, onSuccess: onChanged })
  return <li className={`home-loan-tag-row${tag.category ? '' : ' home-loan-needs'}`}>
    <div><strong><span className="home-loan-swatch" style={{ backgroundColor: tag.color }} />{tag.name}</strong>
      <span>{tag.payments.length} {tag.payments.length === 1 ? 'payment' : 'payments'} · {exactAmount(tag.monthlyAverage, currency)} a month · {exactAmount(tag.total, currency)}{tag.category ? '' : ' · choose its line on the lender’s form'}</span></div>
    <form className="home-loan-apply" onSubmit={event => { event.preventDefault(); save.mutate({ tagId: tag.tagId, category: choice || null }) }}>
      <label className="home-loan-counts-as">Counts as<span className="visually-hidden"> for {tag.name}</span><select value={choice} onChange={event => setChoice(event.target.value)}>
        <option value="">Choose a line on the form</option>
        <CategoryOptions categories={categories} />
      </select></label>
      <button type="submit" className={choice === (tag.category ?? '') ? 'home-loan-secondary' : ''} disabled={save.isPending || choice === (tag.category ?? '')}>Save</button>
    </form>
    <details className="home-loan-payments">
      <summary>Show payments<span className="visually-hidden"> tagged {tag.name}</span> and give one its own line</summary>
      <ul>{tag.payments.map(x => <li key={x.transactionId}><span>{dayLabel(x.date)} · {x.account}</span><span>{x.description}</span><strong>{exactAmount(x.amount, currency)}</strong>
        <PaymentCategory payment={x} categories={categories} label={`${x.description} on ${dayLabel(x.date)}`} onChanged={onChanged} compact /></li>)}</ul>
    </details>
    {save.isError && <p role="alert">That category could not be saved. Try again.</p>}
  </li>
}

function PaymentCategory({ payment, categories, label, onChanged, compact = false }: {
  payment: Pick<HomeLoanPayment, 'transactionId' | 'ownCategory'>; categories: LenderCategory[]; label: string; onChanged: () => Promise<unknown>; compact?: boolean
}) {
  const [choice, setChoice] = useState(payment.ownCategory ?? '')
  const save = useMutation({ mutationFn: savePaymentCategory, onSuccess: onChanged })
  return <form className={`home-loan-apply${compact ? ' home-loan-compact' : ''}`} onSubmit={event => { event.preventDefault(); save.mutate({ transactionId: payment.transactionId, category: choice || null }) }}>
    <select aria-label={`Lender category for ${label}`} value={choice} onChange={event => setChoice(event.target.value)}>
      <option value="">Use its tags</option>
      <CategoryOptions categories={categories} />
    </select>
    <button type="submit" className="home-loan-secondary" disabled={save.isPending || choice === (payment.ownCategory ?? '')}>Save</button>
    {save.isError && <span role="alert">Not saved. Try again.</span>}
  </form>
}

function FlagSection({ data }: { data: HomeLoanReport }) {
  return <section className="panel home-loan-section"><h2>Things a lender may ask about</h2>
    {data.flags.length === 0 ? <p>No large one-off deposits in this period.</p>
      : <>
        <p>None of these decide an application on their own, but lenders usually ask about them. Have an explanation ready.</p>
        {data.flags.map(flag => <details key={flag.kind} className="home-loan-flag">
          <summary><strong>{flag.label}</strong> · {flag.count} {flag.count === 1 ? 'payment' : 'payments'} · {exactAmount(Math.abs(flag.total), data.currency)}</summary>
          <p>{flag.advice}</p>
          <ul className="home-loan-list">{flag.items.map((item, index) => <li key={`${item.date}-${index}`}>
            <div><strong>{item.description}</strong><span>{dayLabel(item.date)} · {item.account}</span></div><strong>{exactAmount(item.amount, data.currency)}</strong>
          </li>)}</ul>
        </details>)}
      </>}
  </section>
}

function Payments({ payments, currency, label }: { payments: HomeLoanPayment[]; currency: string; label: string }) {
  return <details className="home-loan-payments">
    <summary>Show payments<span className="visually-hidden"> from {label}</span></summary>
    <ul>{payments.map(x => <li key={x.transactionId}><span>{dayLabel(x.date)} · {x.account}</span><span>{x.description}</span><strong>{exactAmount(x.amount, currency)}</strong></li>)}</ul>
  </details>
}
