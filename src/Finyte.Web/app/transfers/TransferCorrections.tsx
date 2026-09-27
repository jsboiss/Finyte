import { Help } from '../shared/Help'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { isAxiosError } from 'axios'
import { ArrowRightLeft } from '../shared/Icons'
import { useState } from 'react'
import { httpClient } from '../api/httpClient'
import { dateLabel, shiftDate, todayDate } from '../shared/calendar'
import { exactAmount } from '../shared/formatters'
import { reclassifyTransfers, reviewTransfer, transferQueryKeys, transferSourceLabel, type TransferAction, type TransferPage, type TransferRow } from './transfersApi'

const views = [
  { id: 'transfers', label: 'Internal transfers' },
  { id: 'excluded', label: 'Marked as not a transfer' },
]

export function TransferCorrections({ initialView = 'transfers' }: { initialView?: string }) {
  const queryClient = useQueryClient()
  const [view, setView] = useState(views.some(x => x.id === initialView) ? initialView : 'transfers')
  const [from, setFrom] = useState(() => shiftDate(todayDate(), -89))
  const [to, setTo] = useState(todayDate)
  const [page, setPage] = useState(1)
  const [notice, setNotice] = useState('')
  const rows = useQuery({
    queryKey: ['internal-transfers', view, from, to, page],
    queryFn: () => httpClient<TransferPage>({ url: '/api/internal-transfers', params: { view, from, to, page } }),
    enabled: Boolean(from && to),
  })
  const invalidate = () => Promise.all(transferQueryKeys.map(x => queryClient.invalidateQueries({ queryKey: [x] })))
  const decision = useMutation({
    mutationFn: ({ row, action }: { row: TransferRow; action: TransferAction }) => reviewTransfer(row.id, action),
    onSuccess: async (_data, { action }) => {
      setNotice(action === 'exclude' ? 'Marked as not a transfer. It now counts in spending or income.' : 'Reset. Detection will run again from the description.')
      setPage(1)
      await invalidate()
    },
    onError: () => { void queryClient.invalidateQueries({ queryKey: ['internal-transfers'] }) },
  })
  const reclassify = useMutation({
    mutationFn: reclassifyTransfers,
    onSuccess: async result => { setNotice(`Detection re-run. ${result.changed} ${result.changed === 1 ? 'transaction' : 'transactions'} changed.`); await invalidate() },
  })

  return (
    <section className="transfer-corrections">
      <Help title="How transfers are detected"><p>A transaction is an internal transfer when its description names another of your accounts, for example “Transfer to xx6486” when an account ending in 6486 is connected. Each transaction is decided on its own; nothing is guessed from amounts or dates.</p><p>Internal transfers are excluded from income and spending. Balances and <Link to="/transactions">original transactions</Link> stay unchanged. Mark or unmark any transaction from the ledger; your choice survives syncs and imports.</p></Help>
      <div className="transfer-views" aria-label="Transfer views">
        {views.map(x => <button key={x.id} type="button" aria-pressed={view === x.id} onClick={() => { setView(x.id); setPage(1); setNotice(''); decision.reset() }}>{x.label}</button>)}
      </div>
      <div className="transfer-filters">
        <label>Posted from<input type="date" value={from} onChange={x => { setFrom(x.target.value); setPage(1) }} /></label>
        <label>Through<input type="date" value={to} onChange={x => { setTo(x.target.value); setPage(1) }} /></label>
        <button type="button" disabled={rows.isFetching || decision.isPending} onClick={() => void rows.refetch()}>Refresh</button>
        <button type="button" className="transfer-secondary" disabled={reclassify.isPending} onClick={() => reclassify.mutate()}>{reclassify.isPending ? 'Re-running…' : 'Re-run detection'}</button>
      </div>
      {notice && <p role="status" className="transfer-notice">{notice}</p>}
      {decision.error && <p role="alert">{readError(decision.error)}</p>}
      {reclassify.error && <p role="alert">{readError(reclassify.error)}</p>}
      {rows.isError && <p role="alert">{readError(rows.error)}</p>}
      {rows.isLoading && <p>Loading transfers…</p>}
      {rows.data?.totalCount === 0 && <section className="panel"><h2>{view === 'transfers' ? 'No internal transfers in this date range' : 'Nothing marked as not a transfer'}</h2><p>{view === 'transfers' ? 'Detection needs the other account connected or imported with its account number. You can also mark a transaction from the ledger.' : 'Transactions you mark as not a transfer appear here so you can undo the choice.'}</p></section>}
      <div className="transfer-list">
        {(rows.data?.items ?? []).map(row => (
          <article className="panel transfer-pair" key={row.id}>
            <div className="transfer-pair-heading"><h2>{row.counterpartyAccountName ? `${row.amount < 0 ? 'To' : 'From'} ${row.counterpartyAccountName}` : 'Not a transfer'}</h2><span className="transfer-reviewed">{transferSourceLabel(row.source)}</span></div>
            <div className="transfer-legs">
              <TransferTransaction row={row} />
              {row.counterpartyAccountName && <><ArrowRightLeft aria-hidden="true" /><div className="transfer-leg"><span>{row.amount < 0 ? 'Received by' : 'Sent from'}</span><strong>{row.counterpartyAccountName}</strong></div></>}
            </div>
            <div className="transfer-actions">
              {row.counterpartyAccountId && <button className="transfer-secondary" type="button" disabled={decision.isPending} onClick={() => { setNotice(''); decision.mutate({ row, action: 'exclude' }) }}>Not a transfer</button>}
              {row.source && <button className="transfer-secondary" type="button" disabled={decision.isPending} onClick={() => { setNotice(''); decision.mutate({ row, action: 'reset' }) }}>{row.source === 'excluded' ? 'Undo' : 'Reset to detected'}</button>}
            </div>
          </article>
        ))}
      </div>
      {rows.data && rows.data.totalCount > 0 && <div className="transfer-pagination"><span>{rows.data.totalCount} transactions · Page {page} of {Math.ceil(rows.data.totalCount / rows.data.pageSize)}</span><button disabled={page === 1 || rows.isFetching} onClick={() => setPage(x => x - 1)} type="button">Previous</button><button disabled={page * rows.data.pageSize >= rows.data.totalCount || rows.isFetching} onClick={() => setPage(x => x + 1)} type="button">Next</button></div>}
    </section>
  )
}

function TransferTransaction({ row }: { row: TransferRow }) {
  return <div className="transfer-leg"><span>{row.amount < 0 ? 'Money out' : 'Money in'} · {row.accountName}</span><strong>{exactAmount(row.amount, row.currency)}</strong><p>{row.description}</p><time dateTime={row.postedDate ?? undefined}>{row.postedDate ? dateLabel(row.postedDate) : 'No posting date'}</time></div>
}

function readError(error: Error) {
  if (isAxiosError(error)) {
    const data: unknown = error.response?.data
    if (typeof data === 'string') {
      return data
    }
    if (data && typeof data === 'object' && 'detail' in data && typeof data.detail === 'string') {
      return data.detail
    }
  }
  return 'Unable to complete this request. Refresh and try again.'
}
