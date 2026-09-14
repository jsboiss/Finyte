import { Drawer } from '../shared/Drawer'
import { Help } from '../shared/Help'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { isAxiosError } from 'axios'
import { useState } from 'react'
import { getAccounts, type Account } from '../accounts/accountsApi'
import { httpClient } from '../api/httpClient'
import { getBreakdown, getPayCycles, kinds, money, type PayCycleProfile } from './payCyclesApi'
import './payCycles.css'

const today = () => new Date().toISOString().slice(0, 10)

export function PayCyclesPage() {
  const queryClient = useQueryClient()
  const profiles = useQuery({ queryKey: ['pay-cycles', 'profiles'], queryFn: getPayCycles })
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: getAccounts })
  const [selectedId, setSelectedId] = useState('')
  const [editor, setEditor] = useState<PayCycleProfile | 'new' | null>(null)
  const profile = profiles.data?.find(x => x.id === selectedId) ?? profiles.data?.[0]
  const [message, setMessage] = useState('')
  const remove = useMutation({
    mutationFn: (item: PayCycleProfile) => httpClient<void>({ method: 'DELETE', url: `/api/pay-cycles/${item.id}`, params: { expectedVersion: item.version } }),
    onSuccess: async () => { setMessage('Pay-cycle profile deleted.'); setEditor(null); await queryClient.invalidateQueries({ queryKey: ['pay-cycles'] }) },
  })

  return <section className="page pay-cycles-page">
    <header className="page-header"><div><h1>Pay cycles</h1><Help title="How pay cycles work"><p>Choose a known payday and accounts to track. Cycles show recorded activity and savings transfers. Expected income is a comparison, not a balance. Dates use UTC and only posted activity in the selected currency counts.</p></Help></div><button type="button" disabled={!accounts.data?.length || editor !== null} onClick={() => { setEditor('new'); setMessage('') }}>New pay cycle</button></header>

    {message && <p role="status">{message}</p>}
    {(accounts.isLoading || profiles.isLoading) && <p>Loading pay cycles…</p>}
    {(profiles.error || accounts.error) && <p role="alert">{errorMessage(profiles.error ?? accounts.error)} <button type="button" onClick={() => { void profiles.refetch(); void accounts.refetch() }}>Retry</button></p>}
    {accounts.data?.length === 0 && <p><Link to="/imports">Add an account</Link> before creating a pay cycle.</p>}
    {profiles.data?.length === 0 && accounts.data && accounts.data.length > 0 && editor === null && <p>No pay cycles yet. Create a schedule using a known payday.</p>}
    {editor !== null && accounts.data && <Drawer title={editor === 'new' ? 'New pay cycle' : 'Edit pay cycle'} onClose={() => setEditor(null)}><ProfileEditor key={editor === 'new' ? 'new' : `${editor.id}-${editor.version}`} profile={editor === 'new' ? undefined : editor} accounts={accounts.data}
      onCancel={() => setEditor(null)} onReload={async () => { setEditor(null); await profiles.refetch() }}
      onSaved={async item => { setEditor(null); setSelectedId(item.id); setMessage(`${item.name} saved.`); await queryClient.invalidateQueries({ queryKey: ['pay-cycles'] }) }} /></Drawer>}
    {profile && <>
      <div className="pay-cycle-toolbar">
        <label>Pay-cycle profile<select value={profile.id} onChange={event => { setSelectedId(event.target.value); setEditor(null); remove.reset() }}>
          {profiles.data?.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
        </select></label>
        <button type="button" disabled={editor !== null || remove.isPending} onClick={() => { setEditor(profile); setMessage('') }}>Edit schedule</button>
        <button type="button" className="pay-cycle-secondary" disabled={remove.isPending} onClick={() => { if (window.confirm(`Delete ${profile.name}? Its transactions are kept.`)) { remove.mutate(profile) } }}>Delete profile</button>
      </div>
      {remove.error && <p role="alert">{errorMessage(remove.error)} <button type="button" onClick={() => { remove.reset(); void profiles.refetch() }}>Reload profiles</button></p>}
      <Breakdown key={`${profile.id}-${profile.version}`} profile={profile} />
    </>}
  </section>
}

function ProfileEditor({ profile, accounts, onCancel, onSaved, onReload }: {
  profile?: PayCycleProfile; accounts: Account[]; onCancel: () => void; onSaved: (profile: PayCycleProfile) => Promise<void>; onReload: () => Promise<void>
}) {
  const defaultCurrency = profile?.currency ?? accounts.find(x => x.includeInAnalytics)?.currency ?? accounts[0]?.currency ?? 'AUD'
  const [name, setName] = useState(profile?.name ?? '')
  const [frequency, setFrequency] = useState(profile?.frequency ?? 'fortnightly')
  const [anchorDate, setAnchorDate] = useState(profile?.anchorDate ?? today())
  const [currency, setCurrency] = useState(defaultCurrency)
  const [expectedIncome, setExpectedIncome] = useState(profile?.expectedIncome?.toString() ?? '')
  const [accountIds, setAccountIds] = useState(profile?.accountIds ?? accounts.filter(x => x.includeInAnalytics && x.currency === defaultCurrency).map(x => x.id))
  const [savingsIds, setSavingsIds] = useState(profile?.savingsAccountIds ?? [])
  const mutation = useMutation({
    mutationFn: () => httpClient<PayCycleProfile>({ method: profile ? 'PUT' : 'POST', url: profile ? `/api/pay-cycles/${profile.id}` : '/api/pay-cycles',
      data: { name, frequency, anchorDate, currency, expectedIncome: expectedIncome === '' ? null : expectedIncome, accountIds, savingsAccountIds: savingsIds, expectedVersion: profile?.version } }),
    onSuccess: onSaved,
  })
  const toggle = (ids: string[], id: string) => ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]

  return <form className="panel pay-cycle-editor" onSubmit={event => { event.preventDefault(); mutation.mutate() }}>

    {profile && <p>Changes recalculate past and current breakdowns using the new schedule and selection.</p>}
    <fieldset disabled={mutation.isPending}>
      <div className="pay-cycle-fields">
        <label>Name<input required maxLength={120} value={name} onChange={event => setName(event.target.value)} placeholder="Household payday" /></label>
        <label>Frequency<select value={frequency} onChange={event => setFrequency(event.target.value)}><option value="weekly">Weekly</option><option value="fortnightly">Fortnightly</option><option value="monthly">Monthly</option></select></label>
        <label>Known payday (UTC)<input type="date" required min="1900-01-01" max="9998-12-31" value={anchorDate} onChange={event => setAnchorDate(event.target.value)} /></label>
        <label>Currency<select value={currency} onChange={event => { setCurrency(event.target.value); setAccountIds([]); setSavingsIds([]) }}>
          {[...new Set([defaultCurrency, ...accounts.map(x => x.currency)])].sort().map(x => <option key={x}>{x}</option>)}
        </select></label>
        <label>Expected income per cycle (optional)<input type="number" min="0" step="0.01" value={expectedIncome} onChange={event => setExpectedIncome(event.target.value)} placeholder="No target" /></label>
      </div>
      <Help><p>Each cycle starts on payday and ends the day before the next one. Monthly schedules use the same day of month, clamped to the last day in shorter months. No weekend or holiday adjustment is applied.</p></Help>
      <div className="pay-cycle-selections">
        <fieldset><legend>Accounts to track</legend><p>Explicit selection. Dashboard preferences do not change this scope.</p>
          {accounts.filter(x => x.currency === currency).map(x => <label className="pay-cycle-checkbox" key={x.id}><input type="checkbox" checked={accountIds.includes(x.id)} onChange={() => { setAccountIds(toggle(accountIds, x.id)); setSavingsIds(savingsIds.filter(y => y !== x.id)) }} />{x.name}{!x.includeInAnalytics && ' · Excluded from dashboard'}</label>)}
        </fieldset>
        <fieldset><legend>Savings destinations (optional)</legend><p>Confirmed transfers to and from these accounts are shown as savings movements. Destinations sit outside the tracked accounts.</p>
          {accounts.filter(x => x.currency === currency && !accountIds.includes(x.id)).map(x => <label className="pay-cycle-checkbox" key={x.id}><input type="checkbox" checked={savingsIds.includes(x.id)} onChange={() => setSavingsIds(toggle(savingsIds, x.id))} />{x.name}</label>)}
          {accounts.every(x => x.currency !== currency || accountIds.includes(x.id)) && <p>No other accounts in this currency.</p>}
        </fieldset>
      </div>
      <div className="pay-cycle-toolbar"><button type="submit" disabled={accountIds.length === 0}>{mutation.isPending ? 'Saving…' : 'Save pay cycle'}</button><button type="button" className="pay-cycle-secondary" onClick={onCancel}>Cancel</button></div>
    </fieldset>
    {mutation.error && <p role="alert">{errorMessage(mutation.error)} {isAxiosError(mutation.error) && mutation.error.response?.status === 409 && <button type="button" onClick={() => void onReload()}>Discard edits and reload</button>}</p>}
  </form>
}

function Breakdown({ profile }: { profile: PayCycleProfile }) {
  const [date, setDate] = useState(today())
  const [draftDate, setDraftDate] = useState(today())
  const [page, setPage] = useState(1)
  const [kind, setKind] = useState('')
  const query = useQuery({ queryKey: ['pay-cycles', 'breakdown', profile.id, date, page, kind], queryFn: () => getBreakdown(profile.id, date, page, kind) })
  const data = query.data
  const go = (next: string) => { setDate(next); setDraftDate(next); setPage(1) }
  const amount = (value: number) => money(value, profile.currency)

  return <div className="pay-cycle-breakdown">
    <form className="pay-cycle-toolbar" onSubmit={event => { event.preventDefault(); go(draftDate) }}>
      <button type="button" disabled={!data?.previousDate} onClick={() => data?.previousDate && go(data.previousDate)}>Previous cycle</button>
      <label>Cycle containing date<input type="date" required min="1900-01-01" max="9998-12-31" value={draftDate} onChange={event => setDraftDate(event.target.value)} /></label>
      <button type="submit">Show cycle</button><button type="button" className="pay-cycle-secondary" onClick={() => go(today())}>Current cycle</button>
      <button type="button" disabled={!data?.nextDate} onClick={() => data?.nextDate && go(data.nextDate)}>Next cycle</button>
    </form>
    {query.isLoading && <p>Loading breakdown…</p>}
    {query.error && <p role="alert">{errorMessage(query.error)} <button type="button" onClick={() => void query.refetch()}>Retry</button></p>}
    {data && <>
      <section className="panel pay-cycle-intro">
        <h2>{data.from} to {data.to} · {data.periodStatus}</h2>
        <p>{data.observedThrough ? `Recorded activity through ${data.observedThrough} (UTC).` : 'Future cycle. No actual activity is counted yet.'} {data.totals.transactionCount} transactions counted in {profile.currency}.</p>
        <p>Tracked: {data.accounts.map(x => x.name).join(', ') || 'No available accounts'}. Savings destinations: {data.savingsAccounts.map(x => x.name).join(', ') || 'None'}.</p>
        {data.missingAccountIds.length > 0 && <p role="alert">Some saved accounts are no longer available. Edit this profile to review its scope.</p>}
      </section>
      <div className="pay-cycle-metrics">
        <article className="panel"><span>External credits</span><strong>{amount(data.totals.externalCredits)}</strong><Help title="About credits"><p>Income, refunds and other credits. Confirmed transfers are separate.</p></Help></article>
        <article className="panel"><span>Spending</span><strong>{amount(data.totals.spending)}</strong><Help title="About spending"><p>Posted debits excluding confirmed transfers. Credits are not automatically treated as refunds.</p></Help></article>
        <article className="panel"><span>Net moved to savings</span><strong>{amount(data.totals.netSavingsTransfers)}</strong><p>{amount(data.totals.savingsTransfersOut)} out, less {amount(data.totals.savingsTransfersIn)} returned.</p></article>
        <article className="panel"><span>Net account movement</span><strong>{amount(data.totals.netMovement)}</strong><Help title="About net movement"><p>All counted credits minus debits. This is not your account balance or money available to spend.</p></Help></article>
      </div>
      {profile.expectedIncome !== null && <section className="panel pay-cycle-intro"><h2>Expected income comparison</h2><p>Expected: {amount(profile.expectedIncome)}. External credits: {amount(data.totals.externalCredits)}. Difference: {amount(data.totals.expectedIncomeDifference ?? 0)}.</p><p>This compares the full cycle target with activity recorded so far. External credits may include refunds or other income; they are not verified salary.</p></section>}
      <section className="panel pay-cycle-intro"><h2>Confirmed transfers</h2><p>Other transfers out: {amount(data.totals.otherTransfersOut)}. Other transfers in: {amount(data.totals.transfersIn)}. Net movement between tracked accounts: {amount(data.totals.withinScopeTransfers)}.</p><p>Each leg counts on its own posted date, including transfers that cross cycle boundaries. Suggested or outdated matches remain ordinary credits or spending until reviewed. <Link to="/transfers">Review transfers</Link>.</p></section>
      <section className="panel pay-cycle-intro"><h2>Spending by category</h2>{data.spendingCategories.length === 0 ? <p>No spending recorded in this cycle.</p> : <ul className="pay-cycle-categories">{data.spendingCategories.map(x => <li key={x.name}><span>{x.name} · {x.transactionCount} transactions</span><strong>{amount(x.amount)}</strong></li>)}</ul>}</section>
      <details className="panel pay-cycle-intro"><summary>What is excluded</summary><p>{data.unpostedTransactionCount} pending or other unposted transactions and {data.otherCurrencyTransactionCount} posted transactions in another currency within the observed dates. {data.undatedTransactionCount} transactions across these accounts, across all dates, have no posted date and cannot be assigned to any cycle.</p><Help><p>Balances and expected income are never added to actual totals. No currency conversion is performed. Editing this schedule recomputes historical breakdowns.</p></Help></details>
      <section className="panel pay-cycle-intro">
        <div className="pay-cycle-toolbar"><h2>Transactions behind the numbers</h2><label>Show transaction type<select value={kind} onChange={event => { setKind(event.target.value); setPage(1) }}><option value="">All counted transactions</option>{kinds.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label></div>
        <p>{data.transactions.totalCount} matching transactions. Summary figures above always cover all counted transactions in the cycle.</p>
        <ol className="pay-cycle-transactions">{data.transactions.items.map(x => <li key={x.id}>
          <div><strong>{x.description || x.merchantName || 'Transaction'}</strong><span>{x.postedAt.slice(0, 10)} · {x.accountName}</span><span>{kinds.find(y => y[0] === x.kind)?.[1]} · {x.category}</span></div><strong>{amount(x.amount)}</strong>
        </li>)}</ol>
        {data.transactions.totalCount === 0 && <p>No transactions match this selection.</p>}
        <div className="pay-cycle-toolbar"><button type="button" disabled={page <= 1 || query.isFetching} onClick={() => setPage(page - 1)}>Previous page</button><span>Page {page} of {Math.max(1, Math.ceil(data.transactions.totalCount / data.transactions.pageSize))}</span><button type="button" disabled={page * data.transactions.pageSize >= data.transactions.totalCount || query.isFetching} onClick={() => setPage(page + 1)}>Next page</button></div>
      </section>
    </>}
  </div>
}

function errorMessage(error: unknown) {
  if (isAxiosError(error)) {
    const data: unknown = error.response?.data
    if (typeof data === 'string') { return data }
    if (data && typeof data === 'object' && 'detail' in data && typeof data.detail === 'string') { return data.detail }
  }
  return 'Unable to load or save pay cycles. Please try again.'
}
