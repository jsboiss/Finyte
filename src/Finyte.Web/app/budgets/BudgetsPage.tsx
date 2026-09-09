import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import { useState } from 'react'
import { getAccounts } from '../accounts/accountsApi'
import { httpClient } from '../api/httpClient'
import type { TransactionTag } from '../transactions/types'
import './budgets.css'

type Budget = {
  id: string; name: string; limit: number; currency: string; frequency: string; anchorDate: string
  matchMode: string; categories: string[]; tagIds: string[]; accountScope: string; accountIds: string[]; version: number
}
type Period = { from: string; to: string; limit: number; spent: number; remaining: number; usedPercent: number; transactionCount: number; observedThrough: string | null; excludedCurrencies: { currency: string; transactionCount: number }[] }
type Periods = { budgetId: string; version: number; currency: string; periods: Period[] }
type TransactionPage = { totalCount: number; items: { id: string; accountName: string; description: string | null; merchantName: string | null; postedAt: string; amount: number; currency: string }[] }
const today = () => new Date().toISOString().slice(0, 10)
const money = (amount: number, currency: string) => new Intl.NumberFormat('en-AU', { style: 'currency', currency }).format(amount)
const dateLabel = (date: string) => new Date(`${date}T00:00:00Z`).toLocaleDateString('en-AU', { timeZone: 'UTC', day: 'numeric', month: 'short', year: 'numeric' })

export function BudgetsPage() {
  const queryClient = useQueryClient()
  const budgets = useQuery({ queryKey: ['budgets', 'definitions'], queryFn: () => httpClient<Budget[]>({ url: '/api/budgets' }) })
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [editor, setEditor] = useState<Budget | 'new' | null>(null)
  const [notice, setNotice] = useState('')
  const selected = budgets.data?.find(x => x.id === selectedId) ?? budgets.data?.[0]
  const remove = useMutation({
    mutationFn: (budget: Budget) => httpClient({ method: 'DELETE', url: `/api/budgets/${budget.id}`, params: { expectedVersion: budget.version } }),
    onSuccess: async () => { setNotice('Budget deleted. Transactions are preserved.'); setEditor(null); await queryClient.invalidateQueries({ queryKey: ['budgets'] }) },
  })
  return (
    <section className="page budgets-page">
      <header className="page-header"><div><p>Family planning</p><h1>Budgets</h1></div><button type="button" onClick={() => { setEditor('new'); setNotice('') }}>New budget</button></header>
      <section className="panel budget-help">
        <h2>A spending limit with a clear explanation</h2>
        <p>Budgets count posted spending in one currency, excluding confirmed internal transfers and future or undated transactions. Credits are shown elsewhere as income; they are not assumed to be refunds.</p>
        <p>Each period starts fresh. History uses your current limit, selections and account preferences. Budgets can overlap, so their totals should not be added together. Dates use UTC calendar days.</p>
      </section>
      {notice && <p role="status">{notice}</p>}
      {budgets.isPending && <p>Loading budgets…</p>}
      {budgets.error && <p role="alert">{errorMessage(budgets.error)} <button type="button" onClick={() => void budgets.refetch()}>Retry</button></p>}
      {remove.error && <p role="alert">{errorMessage(remove.error)} <button type="button" onClick={() => void budgets.refetch()}>Reload budgets</button></p>}
      {editor && <BudgetEditor key={editor === 'new' ? 'new' : `${editor.id}-${editor.version}`} budget={editor === 'new' ? undefined : editor} onCancel={() => setEditor(null)} onSaved={async budget => {
        setEditor(null); setSelectedId(budget.id); setNotice(`${budget.name} saved.`)
        await queryClient.invalidateQueries({ queryKey: ['budgets'] })
      }} onReload={async () => { await budgets.refetch(); setEditor(null) }} />}
      {budgets.data?.length === 0 && !editor && <section className="panel"><h2>No budgets yet</h2><p>Create a limit for all spending, exact categories, or tagged transactions. You can review every transaction that counts.</p></section>}
      {!!budgets.data?.length && <div className="budget-layout">
        <nav className="panel budget-list" aria-label="Budgets">{budgets.data.map(budget => <button type="button" key={budget.id} aria-current={selected?.id === budget.id ? 'true' : undefined} onClick={() => setSelectedId(budget.id)}>
          <strong>{budget.name}</strong><span>{money(budget.limit, budget.currency)} · {budget.frequency}</span>
        </button>)}</nav>
        {selected && <BudgetDetail key={selected.id} budget={selected} onEdit={() => { setEditor(selected); setNotice('') }} onDelete={() => {
          if (window.confirm(`Delete ${selected.name}? This removes the budget only.`)) { remove.mutate(selected) }
        }} busy={remove.isPending} />}
      </div>}
    </section>
  )
}

function BudgetEditor({ budget, onSaved, onCancel, onReload }: { budget?: Budget; onSaved: (budget: Budget) => Promise<void>; onCancel: () => void; onReload: () => Promise<void> }) {
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: getAccounts })
  const tags = useQuery({ queryKey: ['tags'], queryFn: () => httpClient<TransactionTag[]>({ url: '/api/tags' }) })
  const [name, setName] = useState(budget?.name ?? '')
  const [limit, setLimit] = useState(budget ? String(budget.limit) : '')
  const [currency, setCurrency] = useState(budget?.currency ?? 'AUD')
  const [frequency, setFrequency] = useState(budget?.frequency ?? 'monthly')
  const [anchor, setAnchor] = useState(budget?.anchorDate ?? `${today().slice(0, 7)}-01`)
  const [mode, setMode] = useState(budget?.matchMode ?? 'all')
  const [categories, setCategories] = useState(budget?.categories.join('\n') ?? '')
  const [tagIds, setTagIds] = useState(budget?.tagIds ?? [])
  const [scope, setScope] = useState(budget?.accountScope ?? 'analytics')
  const [accountIds, setAccountIds] = useState(budget?.accountIds ?? [])
  const save = useMutation({
    mutationFn: () => httpClient<Budget>({
      method: budget ? 'PUT' : 'POST', url: budget ? `/api/budgets/${budget.id}` : '/api/budgets',
      data: { name, limit, currency, frequency, anchorDate: anchor, matchMode: mode,
        categories: mode === 'selected' ? categories.split('\n').map(x => x.trim()).filter(Boolean) : [],
        tagIds: mode === 'selected' ? tagIds : [], accountScope: scope, accountIds: scope === 'selected' ? accountIds : [], expectedVersion: budget?.version },
    }),
    onSuccess: onSaved,
  })
  return <form className="panel budget-editor" onSubmit={event => { event.preventDefault(); save.mutate() }}>
    <h2>{budget ? `Edit ${budget.name}` : 'New budget'}</h2>
    <fieldset disabled={save.isPending}>
      <div className="budget-form-grid">
        <label>Name<input required maxLength={120} value={name} onChange={event => setName(event.target.value)} /></label>
        <label>Limit per period<input required type="number" inputMode="decimal" min="0.01" step="0.01" value={limit} onChange={event => setLimit(event.target.value)} /></label>
        <label>Currency<input required pattern="[A-Za-z]{3}" maxLength={3} value={currency} onChange={event => setCurrency(event.target.value.toUpperCase())} /></label>
        <label>Period<select value={frequency} onChange={event => setFrequency(event.target.value)}><option value="weekly">Weekly</option><option value="fortnightly">Fortnightly</option><option value="monthly">Monthly</option></select></label>
        <label>Known period start<input type="date" required min="1901-01-01" max="9990-12-31" value={anchor} onChange={event => setAnchor(event.target.value)} /></label>
        <label>Spending to count<select value={mode} onChange={event => setMode(event.target.value)}><option value="all">All spending</option><option value="selected">Selected categories or tags</option></select></label>
      </div>
      <p>The start date anchors the repeating schedule, including past periods. Monthly schedules clamp to the last day of shorter months, then return to the original day.</p>
      {mode === 'selected' && <>
        <label>Exact category names, one per line<textarea rows={3} value={categories} onChange={event => setCategories(event.target.value)} placeholder={'Groceries\nEating Out'} /></label>
        <p>A transaction counts once if either its primary or secondary category exactly matches a name (ignoring case), or it has any selected tag.</p>
        <fieldset className="budget-choices"><legend>Tags</legend>{tags.data?.map(tag => <label key={tag.id}><input type="checkbox" checked={tagIds.includes(tag.id)} onChange={event => setTagIds(x => event.target.checked ? [...x, tag.id] : x.filter(y => y !== tag.id))} />{tag.name}</label>)}
          {tags.isPending && <p>Loading tags…</p>}{tags.data?.length === 0 && <p>Create tags on the Transactions page, or use exact categories.</p>}
          {tags.error && <p role="alert">Unable to load tags. <button type="button" onClick={() => void tags.refetch()}>Retry</button></p>}
        </fieldset>
      </>}
      <label>Accounts<select value={scope} onChange={event => setScope(event.target.value)}><option value="analytics">Use accounts included in combined spending</option><option value="selected">Choose specific accounts</option></select></label>
      <p>{scope === 'analytics' ? 'Follows your family’s account preferences, including future accounts.' : 'Only selected accounts count, even when excluded from combined spending. Other currencies still do not count.'}</p>
      {scope === 'selected' && <fieldset className="budget-choices"><legend>Specific accounts</legend>{accounts.data?.map(account => <label key={account.id}><input type="checkbox" checked={accountIds.includes(account.id)} onChange={event => setAccountIds(x => event.target.checked ? [...x, account.id] : x.filter(y => y !== account.id))} />{account.name} ({account.currency}){!account.includeInAnalytics && ' · excluded from combined spending'}</label>)}
        {accounts.isPending && <p>Loading accounts…</p>}{accounts.error && <p role="alert">Unable to load accounts. <button type="button" onClick={() => void accounts.refetch()}>Retry</button></p>}
      </fieldset>}
      <div className="budget-actions"><button type="submit">{save.isPending ? 'Saving…' : 'Save budget'}</button><button type="button" onClick={onCancel}>Cancel</button></div>
    </fieldset>
    {save.error && <p role="alert">{errorMessage(save.error)} {isAxiosError(save.error) && save.error.response?.status === 409 && <button type="button" onClick={() => void onReload()}>Discard edits and reload</button>}</p>}
  </form>
}

function BudgetDetail({ budget, onEdit, onDelete, busy }: { budget: Budget; onEdit: () => void; onDelete: () => void; busy: boolean }) {
  const [date, setDate] = useState(today)
  const [auditDate, setAuditDate] = useState<string | null>(null)
  const periods = useQuery({ queryKey: ['budgets', 'periods', budget.id, budget.version, date], queryFn: () => httpClient<Periods>({ url: `/api/budgets/${budget.id}/periods`, params: { date, count: 6 } }), enabled: !!date })
  const current = periods.data?.periods[0]
  return <section className="panel budget-detail">
    <header><h2>{budget.name}</h2><div className="budget-actions"><button type="button" onClick={onEdit}>Edit budget</button><button type="button" onClick={onDelete} disabled={busy}>Delete budget</button></div></header>
    <p>{budget.matchMode === 'all' ? 'All spending' : `${budget.categories.length} exact categories or ${budget.tagIds.length} tags`} · {budget.accountScope === 'analytics' ? 'Accounts included in combined spending' : `${budget.accountIds.length} specific accounts`} · {budget.currency} only</p>
    {budget.matchMode === 'selected' && !budget.categories.length && !budget.tagIds.length && <p role="status">All selected tags have been deleted. This budget counts no transactions until you edit its selections.</p>}
    {budget.accountScope === 'selected' && !budget.accountIds.length && <p role="status">All selected accounts have been deleted. This budget counts no transactions until you edit its selections.</p>}
    <label>Show the period containing<input type="date" required min="1901-01-01" max="9990-12-31" value={date} onChange={event => { setDate(event.target.value); setAuditDate(null) }} /></label>
    {periods.isFetching && <p role="status">Loading period totals…</p>}
    {periods.error && <p role="alert">{errorMessage(periods.error)} <button type="button" onClick={() => void periods.refetch()}>Retry</button></p>}
    {current && <>
      <h3>{dateLabel(current.from)} – {dateLabel(current.to)}</h3>
      <p>{current.observedThrough ? `Actual spending through ${dateLabel(current.observedThrough)}.` : 'Future period: no actual spending yet.'}</p>
      <div className="budget-metrics"><div><span>Limit</span><strong>{money(current.limit, budget.currency)}</strong></div><div><span>Spent</span><strong>{money(current.spent, budget.currency)}</strong></div><div><span>{current.remaining < 0 ? 'Over budget' : 'Remaining'}</span><strong className={current.remaining < 0 ? 'budget-over' : ''}>{money(Math.abs(current.remaining), budget.currency)}</strong></div></div>
      <progress max={100} value={Math.min(100, current.usedPercent)} aria-label={`${current.usedPercent}% of budget used`} />
      <p>{current.usedPercent}% used · {current.transactionCount} transactions</p>
      {current.excludedCurrencies.length > 0 && <p role="status">Excluded from this budget: {current.excludedCurrencies.map(x => `${x.transactionCount} ${x.currency} transactions`).join(', ')}. No currency conversion is performed.</p>}
      <button type="button" onClick={() => setAuditDate(current.from)}>Review counted transactions</button>
      <h3>Period history</h3><p>Recalculated using this budget’s current settings. Unused amounts do not roll over.</p>
      <div className="budget-history">{periods.data!.periods.map(period => <button key={period.from} type="button" onClick={() => setAuditDate(period.from)}><span>{dateLabel(period.from)} – {dateLabel(period.to)}</span><strong>{money(period.spent, budget.currency)} spent</strong><span>{period.transactionCount} transactions · {money(period.remaining, budget.currency)} remaining</span>{period.excludedCurrencies.length > 0 && <span>Excluded: {period.excludedCurrencies.map(x => `${x.transactionCount} ${x.currency} transactions`).join(', ')}</span>}</button>)}</div>
    </>}
    {auditDate && <BudgetAudit key={`${budget.id}-${budget.version}-${auditDate}`} budget={budget} date={auditDate} onClose={() => setAuditDate(null)} />}
  </section>
}

function BudgetAudit({ budget, date, onClose }: { budget: Budget; date: string; onClose: () => void }) {
  const [page, setPage] = useState(1)
  const transactions = useQuery({ queryKey: ['budgets', 'transactions', budget.id, budget.version, date, page], queryFn: () => httpClient<TransactionPage>({ url: `/api/budgets/${budget.id}/transactions`, params: { date, page, pageSize: 10 } }) })
  return <section className="budget-audit" aria-label="Counted transactions"><div className="budget-actions"><h3>Counted transactions · {dateLabel(date)}</h3><button type="button" onClick={onClose}>Close transactions</button></div>
    {transactions.isFetching && <p>Loading transactions…</p>}
    {transactions.error && <p role="alert">{errorMessage(transactions.error)} <button type="button" onClick={() => void transactions.refetch()}>Retry</button></p>}
    {transactions.data && <><p>{transactions.data.totalCount} transactions in this period</p>
      {transactions.data.items.map(transaction => <article key={transaction.id}><div><strong>{transaction.merchantName || transaction.description || 'Transaction'}</strong><p>{dateLabel(new Date(transaction.postedAt).toISOString().slice(0, 10))} · {transaction.accountName}</p></div><strong>{money(-transaction.amount, transaction.currency)}</strong></article>)}
      {!transactions.data.items.length && <p>No transactions on this page.</p>}
      <div className="budget-actions"><button type="button" disabled={page === 1 || transactions.isFetching} onClick={() => setPage(x => x - 1)}>Previous transactions</button><span>Page {page} of {Math.max(1, Math.ceil(transactions.data.totalCount / 10))}</span><button type="button" disabled={page * 10 >= transactions.data.totalCount || transactions.isFetching} onClick={() => setPage(x => x + 1)}>Next transactions</button></div>
    </>}
  </section>
}

function errorMessage(error: Error) {
  if (isAxiosError(error)) {
    const data: unknown = error.response?.data
    if (typeof data === 'string') { return data }
    if (data && typeof data === 'object' && 'detail' in data && typeof data.detail === 'string') { return data.detail }
  }
  return 'Unable to load or save this budget. Please try again.'
}
