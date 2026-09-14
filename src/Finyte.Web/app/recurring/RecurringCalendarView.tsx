import { useQueries } from '@tanstack/react-query'
import { useState } from 'react'
import { httpClient } from '../api/httpClient'
import { ChevronLeft, ChevronRight } from '../shared/Icons'
import { label, money, recurringError, recurringUrl, type Occurrence, type Series } from './recurringApi'

const iso = (date: Date) => date.toISOString().slice(0, 10)
export function RecurringCalendarView({ series, onSelect }: { series: Series[]; onSelect: (id: string, date: string) => void }) {
  const [month, setMonth] = useState(() => new Date().toISOString().slice(0, 7))
  const [accountId, setAccountId] = useState('')
  const from = `${month}-01`
  const start = new Date(`${from}T00:00:00Z`)
  const end = new Date(Date.UTC(start.getUTCFullYear(), start.getUTCMonth() + 1, 0))
  const to = iso(end)
  const visible = series.filter(item => item.state === 'active' && (!accountId || item.accountId === accountId))
  const queries = useQueries({ queries: visible.map(item => ({ queryKey: ['recurring', item.id, 'occurrences', { from, to }], queryFn: () => httpClient<{ items: Occurrence[] }>({ url: `${recurringUrl}/${item.id}/occurrences`, params: { from, to } }) })) })
  const entries = queries.flatMap((query, index) => (query.data?.items ?? []).map(occurrence => ({ series: visible[index], occurrence })))
  const offset = (start.getUTCDay() + 6) % 7
  const move = (count: number) => setMonth(iso(new Date(Date.UTC(start.getUTCFullYear(), start.getUTCMonth() + count, 1))).slice(0, 7))
  return <section className="panel recurring-calendar">
    <div className="recurring-actions"><button type="button" aria-label="Previous month" onClick={() => move(-1)}><ChevronLeft /></button><h2>{start.toLocaleDateString('en-AU', { month: 'long', year: 'numeric', timeZone: 'UTC' })}</h2><button type="button" aria-label="Next month" onClick={() => move(1)}><ChevronRight /></button><button type="button" className="secondary" onClick={() => setMonth(new Date().toISOString().slice(0, 7))}>This month</button></div>
    <label>Account<select value={accountId} onChange={event => setAccountId(event.target.value)}><option value="">All accounts</option>{Array.from(new Map(series.map(item => [item.accountId, item.accountName]))).map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label>
    <p className="recurring-muted">Active schedules · Expected amounts may change.</p>
    {queries.some(query => query.isLoading) && <p role="status">Loading scheduled payments…</p>}
    {queries.some(query => query.isError) && <p role="alert">{recurringError(queries.find(query => query.isError)!.error!)} <button type="button" onClick={() => queries.filter(query => query.isError).forEach(query => void query.refetch())}>Retry</button></p>}
    {!queries.some(query => query.isLoading || query.isError) && entries.length === 0 && <p>No active payments scheduled this month. Discover a pattern or add a recurring payment to get started.</p>}
    <div className="calendar-grid">
      {['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].map(name => <span className="calendar-weekday" key={name}>{name}</span>)}
      {Array.from({ length: offset }, (_, index) => <div aria-hidden="true" className="calendar-spacer" key={`pad-${index}`} />)}
      {Array.from({ length: end.getUTCDate() }, (_, index) => {
        const date = `${month}-${String(index + 1).padStart(2, '0')}`
        const payments = entries.filter(entry => entry.occurrence.date === date)
        return <div className={`calendar-day ${payments.length === 0 ? 'calendar-empty' : ''}`} key={date}><time dateTime={date} aria-current={date === iso(new Date()) ? 'date' : undefined}>{index + 1}<span className="calendar-mobile-month"> {start.toLocaleDateString('en-AU', { month: 'short', timeZone: 'UTC' })}</span></time>{payments.map(({ series: item, occurrence }) => <button className="calendar-payment" key={item.id} type="button" onClick={() => onSelect(item.id, occurrence.date)}><strong>{item.name}</strong><span>{money(occurrence.paidAmount ?? occurrence.expectedAmount, item.currency)}</span><small>{label(occurrence.status)}</small></button>)}</div>
      })}
    </div>
  </section>
}
