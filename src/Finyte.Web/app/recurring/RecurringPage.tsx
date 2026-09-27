import { RecurringOverview } from './RecurringOverview'
import { RecurringCalendarView } from './RecurringCalendarView'
import { Drawer } from '../shared/Drawer'
import { Help } from '../shared/Help'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getAccounts, type Account } from '../accounts/accountsApi'
import { httpClient } from '../api/httpClient'
import { dateLabel, shiftDate, todayDate } from '../shared/calendar'
import { cadences, getSeries, label, money, recurringError, recurringUrl, shortDate, type AliasField, type SeriesKind, type Candidate, type Discovery, type Occurrence, type Page, type Range, type Review, type Series, type Snapshot } from './recurringApi'
import './Recurring.css'

const day = (offset = 0) => shiftDate(todayDate(), offset)

export function RecurringPage() {
  const client = useQueryClient()
  const [accountId, setAccountId] = useState('')
  const [search, setSearch] = useState('')
  const [cadence, setCadence] = useState('')
  const [sort, setSort] = useState('name')
  const [hideEnded, setHideEnded] = useState(true)
  const [view, setView] = useState('tracked')
  const [showDiscoveryFilters, setShowDiscoveryFilters] = useState(false)
  const [selectedDate, setSelectedDate] = useState<string | undefined>()
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [notice, setNotice] = useState('')
  const [range, setRange] = useState<Range>(() => ({ from: day(-1096), to: day() }))
  const [overviewRange] = useState<Range>(() => ({ from: day(-1096), to: day(366) }))
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: getAccounts })
  const series = useQuery({ queryKey: ['recurring', 'series', overviewRange], queryFn: () => getSeries(overviewRange) })
  const selected = series.data?.items.find(x => x.id === selectedId)
  const refresh = async (message = '') => { setNotice(message); await client.invalidateQueries({ queryKey: ['recurring'] }) }

  return <section className="page recurring-page">
    <header className="page-header recurring-heading"><div><h1>Recurring payments</h1><Help title="How recurring payments work"><p>Discover patterns from at least three payments, review the history, then track a series. Add a payment manually if you have less history. Each match stays available for review when its name or price changes.</p></Help></div><button type="button" onClick={() => { setCreating(true); setSelectedId(null); setView('tracked'); setNotice('') }}>Add recurring payment</button></header>

    {notice && <p role="status" className="recurring-notice">{notice}</p>}
    {series.isError && <p role="alert">{recurringError(series.error)}</p>}
    {accounts.isError && <p role="alert">Unable to load accounts. <button type="button" onClick={() => void accounts.refetch()}>Retry accounts</button></p>}
    <div className="recurring-tabs" aria-label="Recurring payment views">
      {[['tracked', 'Tracked payments'], ['discover', 'Discover patterns'], ['calendar', 'Calendar'], ['dismissed', 'Dismissed patterns']].map(([id, name]) => <button key={id} type="button" aria-pressed={view === id} onClick={() => { setView(id); setSelectedId(null); setSelectedDate(undefined); setCreating(false); setNotice('') }}>{name}</button>)}
      <button className="secondary" disabled={series.isFetching} onClick={() => void refresh()} type="button">Refresh</button>
    </div>
    <div className="recurring-view-picker"><label>View<select value={view} onChange={event => { setView(event.target.value); setSelectedId(null); setSelectedDate(undefined); setCreating(false); setNotice('') }}><option value="tracked">Tracked payments</option><option value="discover">Discover patterns</option><option value="calendar">Calendar</option><option value="dismissed">Dismissed patterns</option></select></label><button type="button" disabled={series.isFetching} onClick={() => void refresh()}>Refresh</button></div>
    {creating && <Drawer title="Add recurring payment" onClose={() => setCreating(false)}><SeriesEditor accounts={accounts.data ?? []} onCancel={() => setCreating(false)} onSaved={async created => { setCreating(false); setSelectedId(created.id); await refresh('Recurring payment created. Review its payment history below.') }} /></Drawer>}
    {view === 'tracked' && <>
      {!selected && <RecurringOverview accounts={accounts.data ?? []} range={overviewRange} onDiscover={() => setView('discover')} onChanged={refresh}
        onSelect={(id, date) => { setSelectedId(id); setSelectedDate(date); setCreating(false); setNotice('') }} />}
      {selected && <SeriesDetail key={`${selected.id}-${selectedDate ?? ""}`} series={selected} initialDate={selectedDate} accounts={accounts.data ?? []} onBack={() => setSelectedId(null)} onChanged={refresh} />}
    </>}
    {view === 'calendar' && <RecurringCalendarView series={series.data?.items ?? []} onSelect={(id, date) => { setSelectedId(id); setSelectedDate(date); setView('tracked') }} />}
    {(view === 'discover' || view === 'dismissed') && <>
      <section className="panel"><div className="section-title"><h2>Find patterns</h2><Help title="Pattern search help"><p className="recurring-muted">By default, discovery uses accounts included in combined spending and income. Choose a range up to five years. Three years helps reveal annual payments; a shorter range makes a busy account easier to review.</p></Help></div><div className="recurring-form-grid"><label>Account<select value={accountId} onChange={event => setAccountId(event.target.value)}><option value="">All included accounts</option>{accounts.data?.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}</select></label><label>Find a pattern<input type="search" maxLength={120} value={search} onChange={event => setSearch(event.target.value)} placeholder="Billing name" /></label></div><label className="recurring-check"><input type="checkbox" checked={hideEnded} onChange={event => setHideEnded(event.target.checked)} />Hide ended patterns</label><button type="button" className="secondary" onClick={() => setShowDiscoveryFilters(true)}>Dates, frequency and sort</button>{showDiscoveryFilters && <Drawer title="Pattern filters" onClose={() => setShowDiscoveryFilters(false)}><div className="recurring-form-grid"><label>Frequency<select value={cadence} onChange={event => setCadence(event.target.value)}><option value="">All frequencies</option>{cadences.map(item => <option key={item} value={item}>{label(item)}</option>)}</select></label><label>Sort<select value={sort} onChange={event => setSort(event.target.value)}><option value="name">Name</option><option value="amount">Amount within currency</option></select></label></div><RangeEditor range={range} onChange={setRange} /><button type="button" onClick={() => setShowDiscoveryFilters(false)}>Done</button></Drawer>}<small className="recurring-muted">{range.from} – {range.to}{cadence && ` · ${label(cadence)}`}</small></section>
      <DiscoveryList key={`${view}-${range.from}-${range.to}-${accountId}-${search}-${cadence}-${sort}-${hideEnded}`} hideEnded={hideEnded} accountId={accountId} search={search} cadence={cadence} sort={sort} range={range} dismissed={view === 'dismissed'} accounts={accounts.data ?? []} onTracked={async item => { setView('tracked'); setSelectedId(item.id); await refresh('Series tracked with the payments you selected.') }} onChanged={refresh} />
    </>}
  </section>
}

function SeriesEditor({ series, discovery, accounts, onCancel, onSaved }: { series?: Series; discovery?: Discovery; accounts: Account[]; onCancel: () => void; onSaved: (item: Series) => Promise<void> }) {
  const [name, setName] = useState(series?.name ?? discovery?.name ?? '')
  const [accountId, setAccountId] = useState(series?.accountId ?? discovery?.accountId ?? '')
  const [currency, setCurrency] = useState(series?.currency ?? discovery?.currency ?? 'AUD')
  const [cadence, setCadence] = useState(series?.cadence ?? discovery?.cadence ?? 'monthly')
  const [anchorDate, setAnchorDate] = useState(series?.anchorDate ?? discovery?.anchorDate ?? day())
  const [amount, setAmount] = useState(String(series?.expectedAmount ?? discovery?.expectedAmount ?? ''))
  const [mode, setMode] = useState(series?.amountMode ?? 'fixed')
  const [state, setState] = useState(series?.state ?? 'active')
  const [kind, setKind] = useState<SeriesKind>(series?.kind ?? discovery?.suggestedKind ?? 'subscription')
  const [aliasField, setAliasField] = useState<AliasField>(discovery?.aliasField ?? 'merchant')
  const [alias, setAlias] = useState(discovery?.aliasValue ?? '')
  const [historyIds, setHistoryIds] = useState(() => new Set(discovery?.transactions.map(x => x.snapshot.id) ?? []))
  const mutation = useMutation({
    mutationFn: () => httpClient<Series>({ method: series ? 'PUT' : 'POST', url: series ? `${recurringUrl}/${series.id}` : recurringUrl, data: series
      ? { name, cadence, anchorDate, expectedAmount: Number(amount), amountMode: mode, state, kind, expectedVersion: series.version }
      : { name, accountId, currency, cadence, anchorDate, expectedAmount: Number(amount), amountMode: mode, kind,
        aliases: alias.trim() ? [{ field: aliasField, value: alias.trim() }] : [],
        history: discovery?.transactions.filter(x => historyIds.has(x.snapshot.id)).map(x => ({ transactionId: x.snapshot.id, occurrenceDate: x.occurrenceDate, fingerprint: x.snapshot.fingerprint })) ?? [] } }),
    onSuccess: onSaved,
  })
  return <form className="panel recurring-divider" onSubmit={event => { event.preventDefault(); mutation.mutate() }}>

    <fieldset disabled={mutation.isPending} className="recurring-form-grid">
      <label>Name<input required maxLength={120} value={name} onChange={event => setName(event.target.value)} placeholder="e.g. Music subscription" /></label>
      <label>Account<select required disabled={Boolean(series || discovery)} value={accountId} onChange={event => { setAccountId(event.target.value); setCurrency(accounts.find(x => x.id === event.target.value)?.currency ?? 'AUD') }}><option value="">Choose an account</option>{accounts.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
      <label>Currency<input required minLength={3} maxLength={3} pattern="[A-Z]{3}" disabled={Boolean(series || discovery)} value={currency} onChange={event => setCurrency(event.target.value.toUpperCase())} /></label>
      <label>Expected amount<input required type="number" min="0.01" max="9999999999999.99" step="0.01" value={amount} onChange={event => setAmount(event.target.value)} /></label>
      <label>Amount pattern<select value={mode} onChange={event => setMode(event.target.value)}><option value="fixed">Usually fixed</option><option value="variable">Variable bill</option></select></label>
      <label>Type<select value={kind} onChange={event => setKind(event.target.value as SeriesKind)}><option value="subscription">Subscription</option><option value="bill">Bill or essential</option></select></label>
      <label>Frequency<select value={cadence} disabled={Boolean(discovery)} onChange={event => setCadence(event.target.value)}>{cadences.map(item => <option key={item} value={item}>{label(item)}</option>)}</select></label>
      <label>First scheduled payment<input required type="date" min="1900-01-01" max="9998-12-31" disabled={Boolean(discovery)} value={anchorDate} onChange={event => setAnchorDate(event.target.value)} /></label>
      {series && <label>State<select value={state} onChange={event => setState(event.target.value)}>{['active', 'paused', 'cancelled'].map(item => <option key={item} value={item}>{item}</option>)}</select></label>}
      {!series && <details className="recurring-advanced"><summary>Advanced matching</summary><div className="recurring-form-grid"><label>Match future payments by<select value={aliasField} onChange={event => setAliasField(event.target.value as AliasField)}><option value="merchant">Merchant name</option><option value="description">Full description</option></select></label><label>Approved billing name (optional)<input maxLength={512} value={alias} onChange={event => setAlias(event.target.value)} /></label></div></details>}
    </fieldset>
    <Help><p className="recurring-muted">Tracking starts from the first scheduled payment date. A date on the last day of a month repeats at month end; other days return to their original day after a short month. Late postings do not move the schedule. Dates are the calendar days your bank reports. Expected amounts remain under your control when prices change.</p></Help>
    {series && <Help><p className="recurring-muted">Changing the schedule can make previous confirmations need review. Pausing or cancelling preserves payment history. To move accounts, create a separate series for the new account.</p></Help>}
    {discovery && <div className="recurring-ledger"><h3>Confirm the payments that belong together</h3>{discovery.transactions.map(item => <label className="recurring-payment" key={item.snapshot.id}><span className="recurring-check"><input type="checkbox" checked={historyIds.has(item.snapshot.id)} onChange={event => setHistoryIds(previous => { const next = new Set(previous); if (event.target.checked) { next.add(item.snapshot.id) } else { next.delete(item.snapshot.id) } return next })} />Include payment for {item.occurrenceDate}</span><SnapshotView snapshot={item.snapshot} /></label>)}</div>}
    {mutation.error && <p role="alert">{recurringError(mutation.error)}</p>}
    <div className="recurring-actions"><button disabled={mutation.isPending || (!series && !accountId)} type="submit">{mutation.isPending ? 'Saving…' : series ? 'Save changes' : discovery ? `Track and confirm ${historyIds.size} payments` : 'Create series'}</button><button type="button" className="secondary" disabled={mutation.isPending} onClick={onCancel}>Cancel</button></div>
  </form>
}

function DiscoveryList({ range, dismissed, accounts, onTracked, onChanged, accountId, search, cadence, sort, hideEnded }: { accountId: string; search: string; cadence: string; sort: string; hideEnded: boolean; range: Range; dismissed: boolean; accounts: Account[]; onTracked: (item: Series) => Promise<void>; onChanged: (message?: string) => Promise<void> }) {
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<string | null>(null)
  const discovery = useQuery({ queryKey: ['recurring', 'discovery', range, dismissed, page, accountId, search, cadence, sort, hideEnded], queryFn: () => httpClient<Page<Discovery>>({ url: `${recurringUrl}/discovery`, params: { ...range, dismissed, page, pageSize: 10, accountId: accountId || undefined, search: search || undefined, cadence: cadence || undefined, sort, hideEnded } }), enabled: Boolean(range.from && range.to) })
  const decision = useMutation({ mutationFn: (candidateKey: string) => httpClient<void>({ method: 'POST', url: `${recurringUrl}/discovery/decisions`, data: { candidateKey, action: dismissed ? 'reset' : 'dismiss' } }), onSuccess: async () => { setPage(1); await onChanged(dismissed ? 'Pattern returned to discovery.' : 'Pattern dismissed. You can restore it from Dismissed patterns.') } })
  return <div className="recurring-list">
    {discovery.isLoading && <p>Looking for recurring patterns…</p>}
    {discovery.error && <p role="alert">{recurringError(discovery.error)}</p>}
    {decision.error && <p role="alert">{recurringError(decision.error)}</p>}
    {discovery.data?.totalCount === 0 && <section className="panel"><h2>{dismissed ? 'No dismissed patterns in this range' : 'No recurring patterns found in this range'}</h2><Help><p className="recurring-muted">A changing billing name, missing history or several charges on similar dates can prevent discovery. You can add a recurring payment yourself and review transactions around its schedule.</p></Help></section>}
    {discovery.data?.items.map(item => <article className="panel discovery-row" key={`${item.key}-${item.transactions[0]?.snapshot.id}`}>
      <div className="recurring-heading"><div><h2>{item.name}</h2><p className="recurring-muted">{item.accountName} · {label(item.cadence)} · {money(item.expectedAmount, item.currency)}</p>
        <p className="recurring-badges">{item.isEarly && <span className="recurring-status">Early · 2 payments</span>}{item.isEnded && <span className="recurring-status recurring-warning">Ended · last paid {shortDate(item.transactions[item.transactions.length - 1].occurrenceDate)}</span>}</p></div>
        {selected !== `${item.key}-${item.transactions[0]?.snapshot.id}` && <div className="recurring-actions">{!dismissed && <button type="button" onClick={() => setSelected(`${item.key}-${item.transactions[0]?.snapshot.id}`)}>Review and track</button>}<button type="button" className="secondary" disabled={decision.isPending} onClick={() => decision.mutate(item.key)}>{dismissed ? 'Restore' : 'Dismiss'}</button></div>}
      </div>
      {selected === `${item.key}-${item.transactions[0]?.snapshot.id}` ? <Drawer title="Track recurring payment" onClose={() => setSelected(null)}><SeriesEditor discovery={item} accounts={accounts} onCancel={() => setSelected(null)} onSaved={onTracked} /></Drawer> : <details><summary>Evidence · {item.transactions.length} payments</summary><Evidence items={item.evidence} /><div className="recurring-ledger">{item.transactions.map(payment => <div className="recurring-payment" key={payment.snapshot.id}><SnapshotView snapshot={payment.snapshot} /></div>)}</div></details>}
    </article>)}
    <Pager data={discovery.data} loading={discovery.isFetching} onPage={setPage} />
  </div>
}

function SeriesDetail({ series, accounts, onBack, onChanged, initialDate }: { initialDate?: string; series: Series; accounts: Account[]; onBack: () => void; onChanged: (message?: string) => Promise<void> }) {
  const [editing, setEditing] = useState(false)
  const [view, setView] = useState('candidates')
  const [range, setRange] = useState<Range>(() => initialDate ? { from: shiftDate(initialDate, -3), to: shiftDate(initialDate, 3) } : { from: day(-100), to: day(35) })
  const [page, setPage] = useState(1)
  const [occurrenceDate, setOccurrenceDate] = useState(initialDate ?? '')
  const occurrences = useQuery({ queryKey: ['recurring', series.id, 'occurrences', range], queryFn: () => httpClient<{ items: Occurrence[] }>({ url: `${recurringUrl}/${series.id}/occurrences`, params: range }), enabled: Boolean(range.from && range.to) })
  const candidates = useQuery({ queryKey: ['recurring', series.id, 'candidates', range, occurrenceDate, page], queryFn: () => httpClient<Page<Candidate>>({ url: `${recurringUrl}/${series.id}/transactions`, params: { ...range, occurrenceDate: occurrenceDate || undefined, page, pageSize: 10 } }), enabled: view === 'candidates' && Boolean(range.from && range.to) })
  const history = useQuery({ queryKey: ['recurring', series.id, 'history', page], queryFn: () => httpClient<Page<Review>>({ url: `${recurringUrl}/${series.id}/history`, params: { page, pageSize: 10 } }), enabled: view === 'history' })
  const action = useMutation({
    mutationFn: ({ snapshot, date, decision, learn }: { snapshot: Snapshot; date: string; decision: string; learn?: AliasField }) => httpClient<Series>({ method: 'POST', url: `${recurringUrl}/${series.id}/decisions`, data: { transactionId: snapshot.id, occurrenceDate: date, action: decision, expectedVersion: series.version, fingerprint: snapshot.fingerprint, learnAliasField: learn ?? null } }),
    onSuccess: async (_data, variables) => { await onChanged(variables.decision === 'confirm' ? 'Payment confirmed.' + (variables.learn ? ' The billing name is now recognised for this series.' : '') : variables.decision === 'reject' ? 'Payment excluded from this series. This decision will survive refreshes.' : 'Decision reset. This payment can be reviewed again.') },
    onError: async () => { await onChanged() },
  })
  const aliasRemoval = useMutation({ mutationFn: (aliasId: string) => httpClient<void>({ method: 'DELETE', url: `${recurringUrl}/${series.id}/aliases/${aliasId}`, params: { expectedVersion: series.version } }), onSuccess: async () => { await onChanged('Billing name removed. Past confirmed payments are preserved.') } })
  const busy = action.isPending || aliasRemoval.isPending
  const review = (snapshot: Snapshot, date: string, decision: string, learn?: AliasField) => { action.mutate({ snapshot, date, decision, learn }) }
  return <div className="recurring-list">
    <div className="recurring-actions"><button className="secondary" type="button" onClick={onBack}>All recurring payments</button></div>
    <section className="panel"><div className="recurring-heading"><div><h2>{series.name}</h2><p className="recurring-muted">{series.accountName} · {money(series.expectedAmount, series.currency)} expected · {label(series.cadence)} · {series.state}</p></div><button type="button" className="secondary" onClick={() => setEditing(!editing)}>{editing ? 'Close editor' : 'Edit schedule and amount'}</button></div>
      {series.needsReviewCount > 0 && <p className="recurring-warning">{series.needsReviewCount} {series.needsReviewCount === 1 ? 'confirmation needs' : 'confirmations need'} review. Changed transactions are excluded from recorded payments until reviewed again.</p>}
      {editing && <Drawer title="Edit recurring payment" onClose={() => setEditing(false)}><SeriesEditor key={`${series.id}-${series.version}`} series={series} accounts={accounts} onCancel={() => setEditing(false)} onSaved={async () => { setEditing(false); await onChanged('Recurring payment updated.') }} /></Drawer>}
      <details><summary>Advanced matching · recognised billing names ({series.aliases.length})</summary><Help><p className="recurring-muted">Names are compared in their chosen field, ignoring punctuation and letter case. Confirm a payment and choose “remember” to add another name. Shared billing services still need careful review.</p></Help><div className="recurring-ledger">{series.aliases.map(alias => <div className="recurring-heading recurring-payment" key={alias.id}><span>{alias.field}: {alias.value}</span><button type="button" className="secondary" disabled={busy} onClick={() => aliasRemoval.mutate(alias.id)}>Remove {alias.value}</button></div>)}</div></details>
    </section>
    {action.error && <p role="alert">{recurringError(action.error)}</p>}
    {aliasRemoval.error && <p role="alert">{recurringError(aliasRemoval.error)}</p>}
    <section className="panel"><h2>Scheduled payments</h2><RangeEditor range={range} onChange={value => { setRange(value); setPage(1); setOccurrenceDate('') }} /><Help><p className="recurring-muted">Each window is three days either side of the scheduled date. “No payment found” may mean that account history is incomplete. Payment candidates require your confirmation.</p></Help>
      {occurrences.error && <p role="alert">{recurringError(occurrences.error)}</p>}
      {occurrences.isLoading && <p>Loading schedule…</p>}
      <div className="recurring-occurrences">{occurrences.data?.items.map(item => <div className="recurring-occurrence" key={item.date}><strong>{item.date}</strong><span className={`recurring-status ${item.status === 'needs-review' ? 'recurring-warning' : ''}`}>{label(item.status)}</span><p>{item.paidAmount !== null ? `${money(item.paidAmount, series.currency)} recorded` : `${money(item.expectedAmount, series.currency)} expected`}</p><small className="recurring-muted">{item.windowFrom} – {item.windowTo}</small><button type="button" className="secondary" onClick={() => { setOccurrenceDate(item.date); setView('candidates'); setPage(1) }}>Review this window</button></div>)}</div>
      {occurrences.data?.items.length === 0 && <p>No scheduled dates in this range.</p>}
    </section>
    <div className="recurring-tabs"><button type="button" aria-pressed={view === 'candidates'} onClick={() => { setView('candidates'); setPage(1) }}>Payment candidates</button><button type="button" aria-pressed={view === 'history'} onClick={() => { setView('history'); setPage(1) }}>Review history</button></div>
    {view === 'candidates' ? <>
      <section className="panel"><h2>{occurrenceDate ? `Payments around ${occurrenceDate}` : 'Review account transactions'}</h2><Help><p className="recurring-muted">Suggestions are ranked across the full selected period before paging. Stronger billing-name evidence appears first; possible name changes and competing matches are explained. Confidence is a review aid, not a guarantee. Every payment still needs your confirmation.</p></Help>{occurrenceDate && <div className="recurring-actions"><button type="button" className="secondary" onClick={() => { setOccurrenceDate(''); setPage(1) }}>Show all account transactions in range</button></div>}</section>
      {candidates.error && <p role="alert">{recurringError(candidates.error)}</p>}
      {candidates.isLoading && <p>Loading payment candidates…</p>}
      {candidates.data?.totalCount === 0 && <p>No eligible payment candidates in this range. Import more history or check the scheduled date.</p>}
      {candidates.data?.items.map(item => <CandidateCard key={`${item.snapshot.id}-${item.decisionStatus}-${series.version}`} candidate={item} occurrences={occurrences.data?.items ?? []} busy={busy} seriesId={series.id} onReview={review} />)}
      <Pager data={candidates.data} loading={candidates.isFetching} onPage={setPage} />
    </> : <>
      <Help><p className="recurring-muted">All review events, across all dates. Past evidence is preserved alongside the current transaction. Reset or exclude a current confirmation to move a payment to another series.</p></Help>
      {history.error && <p role="alert">{recurringError(history.error)}</p>}
      {history.isLoading && <p>Loading review history…</p>}
      {history.data?.totalCount === 0 && <p>No payments reviewed yet.</p>}
      {history.data?.items.map(item => <article className="panel" key={item.id}><div className="recurring-heading"><h3>{label(item.action)} · Scheduled {item.occurrenceDate}</h3><span className="recurring-status">{label(item.currentStatus)}</span></div><SnapshotView snapshot={item.snapshot} /><p className="recurring-muted">Reviewed {new Date(item.reviewedAt).toLocaleString()}</p>
        {item.currentStatus === 'needs-review' && item.currentTransaction && <div className="recurring-payment"><h3>Current transaction</h3><SnapshotView snapshot={item.currentTransaction} /><p>Review the current details in Payment candidates to confirm again.</p></div>}
        {!item.currentTransaction && <p className="recurring-warning">The original transaction is no longer available. The review evidence is preserved.</p>}
        {['confirmed', 'rejected', 'needs-review'].includes(item.currentStatus) && <div className="recurring-actions"><button type="button" disabled={busy} className="secondary" onClick={() => review(item.currentTransaction ?? item.snapshot, item.occurrenceDate, 'reset')}>Reset decision</button>{item.currentStatus === 'confirmed' && <button type="button" disabled={busy} className="secondary" onClick={() => review(item.currentTransaction ?? item.snapshot, item.occurrenceDate, 'reject')}>Exclude from this series</button>}</div>}
      </article>)}
      <Pager data={history.data} loading={history.isFetching} onPage={setPage} />
    </>}
  </div>
}

function CandidateCard({ candidate, occurrences, busy, seriesId, onReview }: { candidate: Candidate; occurrences: Occurrence[]; busy: boolean; seriesId: string; onReview: (snapshot: Snapshot, date: string, decision: string, learn?: AliasField) => void }) {
  const [learn, setLearn] = useState('')
  const [scheduledDate, setScheduledDate] = useState(candidate.suggestedOccurrenceDate)
  const { snapshot } = candidate
  const assignedElsewhere = candidate.currentlyAssignedSeriesId && candidate.currentlyAssignedSeriesId !== seriesId
  const occupied = occurrences.some(item => item.date === scheduledDate && item.transactionId && item.transactionId !== snapshot.id)
  return <article className="panel"><div className="recurring-heading"><h3>Scheduled {candidate.suggestedOccurrenceDate}</h3><span className={`recurring-status ${!candidate.aliasMatch || candidate.amountChanged ? 'recurring-warning' : ''}`}>{candidate.decisionStatus ? label(candidate.decisionStatus) : candidate.aliasMatch ? 'Recognised billing name' : 'Unfamiliar billing name'}</span></div>
    <p className="recurring-muted">{candidate.ranking.matchKind === 'review-only' ? 'Manual review' : candidate.ranking.confidence === 'ambiguous' ? 'Ambiguous match — compare alternatives' : `${label(candidate.ranking.confidence)} confidence suggestion`} · {label(candidate.ranking.matchKind)}</p>
    <SnapshotView snapshot={snapshot} /><Evidence items={candidate.evidence} />
    {assignedElsewhere ? <p className="recurring-warning">This payment is reserved by another series. Reset its confirmation there before moving it.</p> : candidate.decisionStatus === 'confirmed' ? <div className="recurring-actions"><button type="button" className="secondary" disabled={busy} onClick={() => onReview(snapshot, candidate.suggestedOccurrenceDate, 'reject')}>Exclude from this series</button><button type="button" className="secondary" disabled={busy} onClick={() => onReview(snapshot, candidate.suggestedOccurrenceDate, 'reset')}>Reset decision</button></div> : candidate.decisionStatus === 'rejected' ? <div className="recurring-actions"><button type="button" className="secondary" disabled={busy} onClick={() => onReview(snapshot, candidate.suggestedOccurrenceDate, 'reset')}>Return to review</button></div> : <>
      <label>Scheduled date this payment belongs to<input type="date" required min="1900-01-01" max="9998-12-31" value={scheduledDate} disabled={busy} onChange={event => setScheduledDate(event.target.value)} /></label>
      {occupied && <p className="recurring-warning">Another payment already occupies this scheduled date. Reset that confirmation before replacing it.</p>}
      <details className="recurring-advanced"><summary>Advanced matching</summary><label>Remember a billing name after confirming<select value={learn} disabled={busy} onChange={event => setLearn(event.target.value)}><option value="">Confirm this payment only</option>{snapshot.merchantName && <option value="merchant">Remember merchant: {snapshot.merchantName}</option>}{snapshot.description && <option value="description">Remember full description: {snapshot.description}</option>}</select></label></details>
      <div className="recurring-actions"><button type="button" disabled={busy || !scheduledDate || occupied} onClick={() => onReview(snapshot, scheduledDate, 'confirm', (learn || undefined) as AliasField | undefined)}>Confirm payment{learn ? ' and remember name' : ''}</button><button type="button" className="secondary" disabled={busy || !scheduledDate} onClick={() => onReview(snapshot, scheduledDate, 'reject')}>Not this recurring payment</button></div>
    </>}
  </article>
}

function SnapshotView({ snapshot }: { snapshot: Snapshot }) {
  return <div><div className="recurring-heading"><strong>{money(Math.abs(snapshot.amount), snapshot.currency)}</strong><time dateTime={snapshot.postedDate ?? undefined}>{snapshot.postedDate ? dateLabel(snapshot.postedDate) : 'No posting date'}</time></div><p>{snapshot.merchantName ?? snapshot.description ?? 'Unnamed payment'}</p>{snapshot.description && snapshot.description !== snapshot.merchantName && <p className="recurring-muted">{snapshot.description}</p>}<p className="recurring-muted">{snapshot.accountName}{snapshot.reference && ` · Reference: ${snapshot.reference}`}</p></div>
}

function Evidence({ items }: { items: string[] }) {
  return <ul className="recurring-evidence">{items.map((item, index) => <li key={index}>{item}</li>)}</ul>
}

function RangeEditor({ range, onChange }: { range: Range; onChange: (range: Range) => void }) {
  return <div className="recurring-form-grid"><label>From<input type="date" min="1900-01-01" max="9998-12-31" value={range.from} onChange={event => onChange({ ...range, from: event.target.value })} /></label><label>Through<input type="date" min="1900-01-01" max="9998-12-31" value={range.to} onChange={event => onChange({ ...range, to: event.target.value })} /></label></div>
}

function Pager({ data, loading, onPage }: { data?: { totalCount: number; page: number; pageSize: number }; loading: boolean; onPage: (page: number) => void }) {
  if (!data || data.totalCount === 0) { return null }
  return <div className="recurring-pager"><span>{data.totalCount} results · Page {data.page} of {Math.ceil(data.totalCount / data.pageSize)}</span><button type="button" className="secondary" disabled={loading || data.page <= 1} onClick={() => onPage(data.page - 1)}>Previous</button><button type="button" className="secondary" disabled={loading || data.page * data.pageSize >= data.totalCount} onClick={() => onPage(data.page + 1)}>Next</button></div>
}
