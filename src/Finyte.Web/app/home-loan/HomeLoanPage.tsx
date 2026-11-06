import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useEffect, useRef, useState } from 'react'
import { httpClient } from '../api/httpClient'
import { Drawer } from '../shared/Drawer'
import { exactAmount } from '../shared/formatters'
import { Help } from '../shared/Help'
import { starterTagNames } from '../tags/tagSuggestionsApi'
import type { TransactionTag } from '../transactions/types'
import { downloadCsv, getHomeLoan, lenderCategories, monthLabel, saveCategory, saveIncomeSource, type HomeLoanPayment, type HomeLoanReport } from './homeLoanApi'
import { HomeLoanSetup } from './HomeLoanSetup'
import { MerchantRow, type TagDone } from './MerchantRow'
import { MoneyFlowChart } from './MoneyFlowChart'
import './homeLoan.css'

const groupLabels = { living: 'Living expense', commitment: 'Commitment', savings: 'Saved', excluded: 'Not counted' } as const
const extraTagNames = ['Rent', 'Savings']
type Recent = { id: number; message: string; undo: () => Promise<unknown>; undone: boolean }

export function HomeLoanPage() {
  const [months, setMonths] = useState(3)
  const [end, setEnd] = useState('')
  const [editing, setEditing] = useState(false)
  const [message, setMessage] = useState('')
  const [recent, setRecent] = useState<Recent[]>([])
  const toast = useRef<HTMLDivElement>(null)
  const queryClient = useQueryClient()
  const report = useQuery({ queryKey: ['home-loan', months, end], queryFn: () => getHomeLoan(months, end), placeholderData: keepPreviousData })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['home-loan'] })
  const refreshTags = () => Promise.all([refresh(), queryClient.invalidateQueries({ queryKey: ['tags'] }), queryClient.invalidateQueries({ queryKey: ['transactions'] })])
  const download = useMutation({ mutationFn: ({ file, data }: { file: 'summary' | 'transactions'; data: HomeLoanReport }) => downloadCsv(file, data) })
  const undo = useMutation({
    mutationFn: (item: Recent) => item.undo(),
    onSuccess: async (_, item) => {
      setRecent(current => current.map(x => x.id === item.id ? { ...x, undone: true } : x))
      await refreshTags()
    },
  })
  const onDone: TagDone = (text, undoAction) => {
    setRecent(current => [{ id: Date.now(), message: text, undo: undoAction, undone: false }, ...current].slice(0, 8))
  }
  useEffect(() => { if (recent.length > 0) { toast.current?.focus() } }, [recent.length])
  const data = report.data
  const latest = recent[0]

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
      <ExpenseSection data={data} onChanged={refreshTags} onDone={onDone} />
      <FlagSection data={data} />
    </>}
    {latest && <div ref={toast} tabIndex={-1} className="home-loan-toast" role="status" aria-label="Last change">
      <span>{latest.undone ? `Undone: ${latest.message}` : latest.message}</span>
      {!latest.undone && <button type="button" className="home-loan-secondary" disabled={undo.isPending} onClick={() => undo.mutate(latest)}>Undo</button>}
      {recent.length > 1 && <details>
        <summary>Recently tagged ({recent.length})</summary>
        <ul>{recent.slice(1).map(item => <li key={item.id}><span>{item.undone ? `Undone: ${item.message}` : item.message}</span>
          {!item.undone && <button type="button" className="home-loan-link" disabled={undo.isPending} onClick={() => undo.mutate(item)}>Undo</button>}</li>)}</ul>
      </details>}
      {undo.isError && <span role="alert">That couldn't be undone. Try again.</span>}
    </div>}
  </section>
}

function Summary({ data }: { data: HomeLoanReport }) {
  const money = (value: number) => exactAmount(value, data.currency)
  const average = data.average
  return <section className="home-loan-metrics" aria-label="Monthly averages">
    <article className="panel"><span>Income: {data.setup.firstApplicant}</span><strong>{money(average.firstApplicantIncome)}</strong><small>a month</small></article>
    {data.setup.secondApplicant && <article className="panel"><span>Income: {data.setup.secondApplicant}</span><strong>{money(average.secondApplicantIncome)}</strong><small>a month</small></article>}
    <article className="panel"><span>Living expenses</span><strong>{money(average.livingExpenses)}</strong><small>a month</small></article>
    <article className="panel"><span>Commitments</span><strong>{money(average.commitments)}</strong><small>rent, loans, cards, buy now pay later</small></article>
    <article className="panel"><span>Saved</span><strong>{money(average.saved)}</strong><small>a month · {money(data.total.saved)} in total</small></article>
    <article className="panel"><span>Left over</span><strong className={average.surplus < 0 ? 'home-loan-negative' : ''}>{money(average.surplus)}</strong><small>a month, after expenses and commitments</small></article>
  </section>
}

function ToDo({ data }: { data: HomeLoanReport }) {
  const unlabelledCredits = data.payers.filter(x => x.kind === null)
  const unlabelledSpend = data.categories.find(x => x.key === 'unlabelled')?.total ?? 0
  const totalSpend = data.categories.filter(x => x.group !== 'excluded').reduce((sum, x) => sum + x.total, 0)
  const percent = totalSpend > 0 ? Math.round(((totalSpend - unlabelledSpend) / totalSpend) * 100) : 100
  if (unlabelledCredits.length === 0 && unlabelledSpend === 0) {
    return <div className="home-loan-callout home-loan-done"><h2>Everything is labelled</h2><p>Every payment in this period has a label. Download the summary when you're ready.</p></div>
  }
  return <section className="home-loan-callout" aria-labelledby="home-loan-todo"><h2 id="home-loan-todo">Still to label</h2>
    <div className="home-loan-progress"><div role="progressbar" aria-label="Spending tagged" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}><span style={{ width: `${percent}%` }} /></div>
      <span>{percent}% of spending tagged</span></div>
    <ul>
      {unlabelledCredits.length > 0 && <li><a href="#home-loan-income">{unlabelledCredits.length} {unlabelledCredits.length === 1 ? 'source' : 'sources'} of money in</a> · {exactAmount(unlabelledCredits.reduce((sum, x) => sum + x.total, 0), data.currency)}</li>}
      {unlabelledSpend > 0 && <li><a href="#home-loan-expenses">{data.needsLabel.length} {data.needsLabel.length === 1 ? 'merchant' : 'merchants'} without a tag</a> · {exactAmount(unlabelledSpend, data.currency)}</li>}
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
    <p>Label each payer once and every payment from them follows. Lenders check that salary arrives regularly and matches your payslips.</p>
    {data.payers.length === 0 && <p>No money came in during this period.</p>}
    <ul className="home-loan-list">{data.payers.map(payer => <PayerRow key={payer.key} payer={payer} options={options} currency={data.currency} onChanged={onChanged} />)}</ul>
  </section>
}

function PayerRow({ payer, options, currency, onChanged }: { payer: HomeLoanReport['payers'][number]; options: [string, string][]; currency: string; onChanged: () => Promise<unknown> }) {
  const saved = payer.kind ? `${payer.kind}:${payer.applicant ?? ''}` : ''
  const known = options.some(([value]) => value === saved)
  const [choice, setChoice] = useState(known ? saved : '')
  const save = useMutation({ mutationFn: saveIncomeSource, onSuccess: onChanged })
  const needs = !payer.kind || !known
  return <li className={needs ? 'home-loan-needs' : ''}>
    <div><strong>{payer.name}{payer.paymentType && <span className="home-loan-pill" data-type={payer.paymentType}>{payer.paymentType}</span>}</strong>
      <span>{payer.count} {payer.count === 1 ? 'payment' : 'payments'} in {payer.monthsSeen} {payer.monthsSeen === 1 ? 'month' : 'months'} · {exactAmount(payer.total, currency)}{needs ? ' · needs a label' : ''}</span>
      <Payments payments={payer.payments} currency={currency} label={payer.name} /></div>
    <form className="home-loan-apply" onSubmit={event => {
      event.preventDefault()
      const [kind, applicant] = choice.split(':')
      save.mutate({ key: payer.key, name: payer.name, kind: kind || null, applicant: applicant ? Number(applicant) : null })
    }}>
      <select aria-label={`Label for ${payer.name}`} value={choice} onChange={event => setChoice(event.target.value)}>
        {options.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
      </select>
      <button type="submit" className={choice === saved ? 'home-loan-secondary' : ''} disabled={save.isPending || choice === saved}>{save.isPending ? 'Saving…' : 'Save'}</button>
    </form>
    {save.isError && <p role="alert">That label could not be saved. Try again.</p>}
  </li>
}

function ExpenseSection({ data, onChanged, onDone }: { data: HomeLoanReport; onChanged: () => Promise<unknown>; onDone: TagDone }) {
  const tags = useQuery({ queryKey: ['tags'], queryFn: () => httpClient<TransactionTag[]>({ url: '/api/tags' }) })
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<'amount' | 'name' | 'count'>('amount')
  const [limit, setLimit] = useState(25)
  const existing = tags.data ?? []
  const starters = [...extraTagNames, ...starterTagNames].filter(x => !existing.some(y => y.name.toLowerCase() === x.toLowerCase()))
  const tagValue = (name: string) => existing.find(y => y.name.toLowerCase() === name.toLowerCase())?.id ?? `new:${name}`
  const money = (value: number) => exactAmount(value, data.currency)
  const term = search.trim().toLowerCase()
  const merchants = data.needsLabel
    .filter(x => !term || x.name.toLowerCase().includes(term) || x.variants.some(y => y.ruleMerchantName.toLowerCase().includes(term)))
    .sort((a, b) => sort === 'name' ? a.name.localeCompare(b.name) : sort === 'count' ? b.count - a.count : b.total - a.total)
  return <section className="panel home-loan-section" id="home-loan-expenses"><h2>Money out</h2>
    <p>Spending is grouped into the categories lenders use on their living expense forms. Rent, loan repayments and buy now pay later are listed as commitments.</p>
    <div className="home-loan-table-wrap"><table className="home-loan-table">
      <thead><tr><th scope="col">Category</th><th scope="col">Type</th><th scope="col">Per month</th><th scope="col">Total</th></tr></thead>
      <tbody>{data.categories.map(x => <tr key={x.key} className={x.key === 'unlabelled' ? 'home-loan-needs-row' : ''}>
        <th scope="row">{x.label}</th><td>{groupLabels[x.group]}</td><td>{money(x.monthlyAverage)}</td><td>{money(x.total)}</td>
      </tr>)}</tbody>
    </table></div>
    {data.categories.length === 0 && <p>No spending in this period.</p>}

    {data.needsLabel.length > 0 && <>
      <h3>Needs a tag ({data.needsLabel.length})</h3>
      <p>Check each vendor name, then tag all its payments at once, or open its payments to tag them one by one. Tagging a vendor also tags its future payments. Use a Savings tag for money moved to savings at another bank, and a Rent tag for rent.</p>
      <div className="home-loan-filters">
        <label>Search vendors<input type="search" value={search} onChange={event => { setSearch(event.target.value); setLimit(25) }} placeholder="e.g. Kmart" /></label>
        <label>Sort by<select value={sort} onChange={event => setSort(event.target.value as 'amount' | 'name' | 'count')}>
          <option value="amount">Amount</option><option value="count">Number of payments</option><option value="name">Name</option>
        </select></label>
      </div>
      {merchants.length === 0 && <p>No vendors match your search.</p>}
      <ul className="home-loan-list">{merchants.slice(0, limit).map(x => <MerchantRow key={`${x.key}-${x.variants.length}`} merchant={x} currency={data.currency}
        existing={existing} starters={starters} suggestedValue={tagValue} onChanged={onChanged} onDone={onDone} />)}</ul>
      {merchants.length > limit && <button type="button" className="home-loan-secondary home-loan-more" onClick={() => setLimit(x => x + 25)}>Show {Math.min(25, merchants.length - limit)} more of {merchants.length - limit}</button>}
    </>}

    {data.tags.length > 0 && <details className="home-loan-tag-map">
      <summary>Lender categories for your tags ({data.tags.length})</summary>
      <p>Each tag counts towards one lender category. Suggested categories are marked.</p>
      <ul className="home-loan-list">{data.tags.map(x => <TagCategoryRow key={`${x.tagId}-${x.category}`} tag={x} currency={data.currency} onChanged={onChanged} />)}</ul>
    </details>}
  </section>
}

function TagCategoryRow({ tag, currency, onChanged }: { tag: HomeLoanReport['tags'][number]; currency: string; onChanged: () => Promise<unknown> }) {
  const [choice, setChoice] = useState(tag.category)
  const save = useMutation({ mutationFn: saveCategory, onSuccess: onChanged })
  return <li>
    <div><strong><span className="home-loan-swatch" style={{ backgroundColor: tag.color }} />{tag.name}</strong><span>{exactAmount(tag.total, currency)}{tag.isDefault ? ' · suggested category' : ''}</span></div>
    <form className="home-loan-apply" onSubmit={event => { event.preventDefault(); save.mutate({ tagId: tag.tagId, category: choice }) }}>
      <select aria-label={`Lender category for ${tag.name}`} value={choice} onChange={event => setChoice(event.target.value)}>
        {(['living', 'commitment', 'savings', 'excluded'] as const).map(group => <optgroup key={group} label={groupLabels[group]}>
          {lenderCategories.filter(y => y.group === group).map(y => <option key={y.key} value={y.key}>{y.label}</option>)}
        </optgroup>)}
      </select>
      <button type="submit" className={choice === tag.category ? 'home-loan-secondary' : ''} disabled={save.isPending || choice === tag.category}>Save</button>
    </form>
    {save.isError && <p role="alert">That category could not be saved. Try again.</p>}
  </li>
}

function FlagSection({ data }: { data: HomeLoanReport }) {
  return <section className="panel home-loan-section"><h2>Things a lender may ask about</h2>
    {data.flags.length === 0 ? <p>Nothing found. No gambling, buy now pay later, dishonour fees, short-term lenders or large one-off deposits in this period.</p>
      : <>
        <p>None of these decide an application on their own, but lenders usually ask about them. Have an explanation ready.</p>
        {data.flags.map(flag => <details key={flag.kind} className="home-loan-flag">
          <summary><strong>{flag.label}</strong> · {flag.count} {flag.count === 1 ? 'payment' : 'payments'} · {exactAmount(Math.abs(flag.total), data.currency)}</summary>
          <p>{flag.advice}</p>
          <ul className="home-loan-list">{flag.items.map((item, index) => <li key={`${item.date}-${index}`}>
            <div><strong>{item.description}</strong><span>{item.date} · {item.account}</span></div><strong>{exactAmount(item.amount, data.currency)}</strong>
          </li>)}</ul>
        </details>)}
      </>}
  </section>
}

function Payments({ payments, currency, label }: { payments: HomeLoanPayment[]; currency: string; label: string }) {
  return <details className="home-loan-payments">
    <summary>Show payments<span className="visually-hidden"> from {label}</span></summary>
    <ul>{payments.map(x => <li key={x.transactionId}><span>{x.date} · {x.account}</span><span>{x.description}</span><strong>{exactAmount(x.amount, currency)}</strong></li>)}</ul>
  </details>
}
