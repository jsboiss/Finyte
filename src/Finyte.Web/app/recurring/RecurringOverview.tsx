import { useMutation, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import type { Account } from '../accounts/accountsApi'
import { httpClient } from '../api/httpClient'
import { getSeries, getUpcoming, label, money, recurringError, recurringUrl, shortDate, type CostSummary, type Discovery, type Page, type Range, type Series, type SeriesKind } from './recurringApi'

const keepActiveKey = 'finyte.recurring.keep-active'

function readKeepActive(): string[] {
  try { return JSON.parse(localStorage.getItem(keepActiveKey) ?? '[]') as string[] } catch { return [] }
}

function writeKeepActive(values: string[]) {
  try { localStorage.setItem(keepActiveKey, JSON.stringify(values)) } catch { return }
}

export function RecurringOverview({ accounts, range, onSelect, onDiscover, onChanged }: { accounts: Account[]; range: Range; onSelect: (id: string, date?: string) => void; onDiscover: () => void; onChanged: (message?: string) => Promise<void> }) {
  const [accountId, setAccountId] = useState('')
  const [keepActive, setKeepActive] = useState(readKeepActive)
  const scope = accountId || undefined
  const series = useQuery({ queryKey: ['recurring', 'series', range, accountId], queryFn: () => getSeries(range, scope) })
  const upcoming = useQuery({ queryKey: ['recurring', 'upcoming', 14, accountId], queryFn: () => getUpcoming(14, scope) })
  const discovery = useQuery({ queryKey: ['recurring', 'discovery-count', accountId], queryFn: () => httpClient<Page<Discovery>>({ url: `${recurringUrl}/discovery`, params: { page: 1, pageSize: 1, hideEnded: true, accountId: scope } }) })
  const stateChange = useMutation({
    mutationFn: ({ item, state }: { item: Series; state: string }) => httpClient<Series>({ method: 'PUT', url: `${recurringUrl}/${item.id}`, data: { name: item.name, cadence: item.cadence, anchorDate: item.anchorDate, expectedAmount: item.expectedAmount, amountMode: item.amountMode, state, kind: item.kind, expectedVersion: item.version } }),
    onSuccess: async (_data, { item, state }) => { await onChanged(state === 'cancelled' ? `${item.name} marked as cancelled.` : `${item.name} marked as active.`) },
    onError: async () => { await onChanged() },
  })
  const items = series.data?.items ?? []
  const current = items.filter(x => x.state !== 'cancelled')
  const cancelled = items.filter(x => x.state === 'cancelled')
  const priceChanges = current.filter(x => x.priceChanged && x.lastPaidAmount !== null)
  const missed = current.filter(x => x.kind === 'subscription' && x.state === 'active' && x.missedOccurrenceDate && !keepActive.includes(`${x.id}:${x.missedOccurrenceDate}`))
  const newPatterns = discovery.data?.totalCount ?? 0
  const setState = (item: Series, state: string) => stateChange.mutate({ item, state })
  const keep = (item: Series) => {
    const next = [...keepActive, `${item.id}:${item.missedOccurrenceDate}`]
    setKeepActive(next)
    writeKeepActive(next)
  }

  return <div className="recurring-overview">
    <label className="recurring-overview-filter">Account<select value={accountId} onChange={event => setAccountId(event.target.value)}><option value="">All accounts</option>{accounts.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}</select></label>
    {series.isError && <p role="alert">{recurringError(series.error)}</p>}
    {stateChange.error && <p role="alert">{recurringError(stateChange.error)}</p>}
    {series.isLoading && <p>Loading recurring payments…</p>}

    {(series.data?.costs ?? []).map(cost => <SummaryStrip key={cost.currency} cost={cost} />)}

    {(newPatterns > 0 || priceChanges.length > 0 || missed.length > 0) && <section className="panel recurring-attention" aria-label="Needs attention">
      <h2>Needs attention</h2>
      <ul>
        {newPatterns > 0 && <li><span>{newPatterns} new {newPatterns === 1 ? 'pattern' : 'patterns'} found</span><button type="button" className="secondary" onClick={onDiscover}>Review</button></li>}
        {priceChanges.map(item => <li key={`price-${item.id}`}><span>{item.name} went from {money(item.expectedAmount, item.currency)} to {money(item.lastPaidAmount!, item.currency)}</span><button type="button" className="secondary" onClick={() => onSelect(item.id)}>Review</button></li>)}
        {missed.map(item => <li key={`missed-${item.id}`}><span>No payment found for {item.name} on {shortDate(item.missedOccurrenceDate!)}. Did you cancel it?</span><span className="recurring-actions"><button type="button" disabled={stateChange.isPending} onClick={() => setState(item, 'cancelled')}>Yes, cancelled</button><button type="button" className="secondary" onClick={() => keep(item)}>Still active</button></span></li>)}
      </ul>
    </section>}

    {(upcoming.data?.items.length ?? 0) > 0 && <section className="panel" aria-label="Next 14 days">
      <h2>Next 14 days</h2>
      <div className="recurring-chips">{upcoming.data!.items.map(item => <button type="button" className="recurring-chip" key={`${item.seriesId}-${item.date}`} onClick={() => onSelect(item.seriesId, item.date)}><time dateTime={item.date}>{shortDate(item.date)}</time><strong>{item.name}</strong><span>{money(item.expectedAmount, item.currency)}</span><small>{item.accountName}</small></button>)}</div>
    </section>}

    {items.length === 0 && series.isSuccess && <section className="panel"><h2>Find your regular payments</h2><p className="recurring-muted">1. Find patterns. 2. Review the payments. 3. Track the series.</p><div className="recurring-actions"><button onClick={onDiscover} type="button">Discover patterns</button></div></section>}

    {current.length > 0 && <div className="recurring-columns">
      <SeriesGroup title="Subscriptions" kind="subscription" items={current} costs={series.data?.costs ?? []} busy={stateChange.isPending} onSelect={onSelect} onState={setState} />
      <SeriesGroup title="Bills and essentials" kind="bill" items={current} costs={series.data?.costs ?? []} busy={stateChange.isPending} onSelect={onSelect} onState={setState} />
    </div>}

    {cancelled.length > 0 && <details className="panel"><summary>Cancelled ({cancelled.length})</summary><ul className="recurring-rows">{cancelled.map(item => <SeriesRow key={item.id} item={item} busy={stateChange.isPending} onSelect={onSelect} onState={setState} />)}</ul></details>}
  </div>
}

function SummaryStrip({ cost }: { cost: CostSummary }) {
  return <section className="recurring-summary" aria-label={`${cost.currency} recurring costs`}>
    <div><span>Per month</span><strong>{money(cost.monthlyEstimate, cost.currency)}</strong><small>{money(cost.annualEstimate, cost.currency)} / year</small></div>
    <div><span>Subscriptions</span><strong>{money(cost.subscriptionMonthlyEstimate, cost.currency)}</strong></div>
    <div><span>Bills and essentials</span><strong>{money(cost.billMonthlyEstimate, cost.currency)}</strong></div>
  </section>
}

function SeriesGroup({ title, kind, items, costs, busy, onSelect, onState }: { title: string; kind: SeriesKind; items: Series[]; costs: CostSummary[]; busy: boolean; onSelect: (id: string) => void; onState: (item: Series, state: string) => void }) {
  const rows = items.filter(x => x.kind === kind)
  const total = costs.map(x => money(kind === 'subscription' ? x.subscriptionMonthlyEstimate : x.billMonthlyEstimate, x.currency)).join(' + ')
  return <section className="panel recurring-group">
    <div className="recurring-heading"><h2>{title} <span className="recurring-muted">· {rows.length}</span></h2>{total && <span className="recurring-muted">{total} / month</span>}</div>
    {rows.length === 0 ? <p className="recurring-muted">Nothing tracked yet.</p> : <ul className="recurring-rows">{rows.map(item => <SeriesRow key={item.id} item={item} busy={busy} onSelect={onSelect} onState={onState} />)}</ul>}
  </section>
}

function SeriesRow({ item, busy, onSelect, onState }: { item: Series; busy: boolean; onSelect: (id: string) => void; onState: (item: Series, state: string) => void }) {
  return <li className="recurring-row">
    <button type="button" className="recurring-row-main" onClick={() => onSelect(item.id)}>
      <strong>{item.name}</strong>
      <span className="recurring-muted">{item.accountName} · {label(item.cadence)}{item.nextDueDate ? ` · next ${shortDate(item.nextDueDate)}` : ''}</span>
      <span className="recurring-badges">
        {item.priceChanged && <span className="recurring-status recurring-warning">Price changed</span>}
        {item.state === 'active' && item.missedOccurrenceDate && <span className="recurring-status recurring-warning">Missed</span>}
        {item.amountMode === 'variable' && <span className="recurring-status">Variable</span>}
        {item.state === 'paused' && <span className="recurring-status">Paused</span>}
        {item.needsReviewCount > 0 && <span className="recurring-status recurring-warning">Needs review</span>}
      </span>
    </button>
    <span className="recurring-row-amount">{money(item.expectedAmount, item.currency)}</span>
    {item.kind === 'subscription' && <label className="recurring-switch"><input type="checkbox" role="switch" checked={item.state !== 'cancelled'} disabled={busy} onChange={event => onState(item, event.target.checked ? 'active' : 'cancelled')} />Still active</label>}
  </li>
}
