import { Help } from '../shared/Help'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { isAxiosError } from 'axios'
import { ArrowRightLeft } from '../shared/Icons'
import { useState } from 'react'
import { httpClient } from '../api/httpClient'
import { exactAmount } from '../shared/formatters'

type TransferLeg = { id: string; accountId: string; accountName: string; description: string; amount: number; currency: string; postedAt: string | null }
type TransferReview = {
  debit: TransferLeg
  credit: TransferLeg
  status: string
  isAmbiguous: boolean
  explanation: string
  reviewedAt: string | null
}
type ReviewPage = { items: TransferReview[]; totalCount: number; page: number; pageSize: number }
type ReviewAction = 'confirm' | 'dismiss' | 'reset'
const views = [
  { id: 'suggested', label: 'Possible matches' },
  { id: 'confirmed', label: 'Matched transfers' },
  { id: 'needs-review', label: 'Changed or conflicting' },
  { id: 'dismissed', label: 'Dismissed pairs' },
]

export function TransferCorrections({ initialView = 'confirmed' }: { initialView?: string }) {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState(initialView)
  const [from, setFrom] = useState(() => new Date(Date.now() - 89 * 86400000).toISOString().slice(0, 10))
  const [to, setTo] = useState(() => new Date().toISOString().slice(0, 10))
  const [page, setPage] = useState(1)
  const [notice, setNotice] = useState('')
  const reviews = useQuery({
    queryKey: ['internal-transfers', status, from, to, page],
    queryFn: () => httpClient<ReviewPage>({ url: '/api/internal-transfers', params: { status, from, to, page } }),
    enabled: Boolean(from && to),
  })
  const decision = useMutation({
    mutationFn: ({ pair, action }: { pair: TransferReview; action: ReviewAction }) => httpClient<void>({
      method: 'POST', url: '/api/internal-transfers/review',
      data: {
        debitTransactionId: pair.debit.id, creditTransactionId: pair.credit.id, action,
        amount: pair.credit.amount, currency: pair.credit.currency,
        debitAccountId: pair.debit.accountId, creditAccountId: pair.credit.accountId,
        debitPostedAt: pair.debit.postedAt, creditPostedAt: pair.credit.postedAt,
      },
    }),
    onSuccess: async (_data, { action }) => {
      setNotice(action === 'confirm' ? 'Transfer confirmed. Both transactions are excluded from spending and income. Dashboard totals are refreshing.'
        : action === 'dismiss' ? 'Pair dismissed. Both transactions remain included in spending and income.'
        : 'Decision undone. These transactions count normally again. Automatic matches stay dismissed until returned to review.')
      setPage(1)
      await Promise.all(['internal-transfers', 'transactions', 'overview', 'cash-flow', 'budgets', 'pay-cycles'].map(x => queryClient.invalidateQueries({ queryKey: [x] })))
    },
    onError: () => { void queryClient.invalidateQueries({ queryKey: ['internal-transfers'] }) },
  })

  function review(pair: TransferReview, action: ReviewAction) {
    setNotice('')
    decision.mutate({ pair, action })
  }

  return (
    <section className="transfer-corrections">
      <Help title="How transfers are matched"><p>Automatic matches need unique equal amounts in the same currency on different household accounts, within three days, plus transfer descriptions or a shared reference with transfer evidence. Imports and bank syncs trigger matching; existing history is checked by the worker.</p><p>Matched transfers are excluded from income and spending. Balances and <Link to="/transactions">original transactions</Link> stay unchanged. You can undo a match; it will stay dismissed until you return it to review.</p></Help>
      <div className="transfer-views" aria-label="Transfer review views">
        {views.map(x => <button key={x.id} type="button" aria-pressed={status === x.id} onClick={() => { setStatus(x.id); setPage(1); setNotice(''); decision.reset() }}>{x.label}</button>)}
      </div>
      <div className="transfer-filters">
        <label>Money-out date from<input type="date" value={from} disabled={status === 'needs-review'} onChange={x => { setFrom(x.target.value); setPage(1) }} /></label>
        <label>Through<input type="date" value={to} disabled={status === 'needs-review'} onChange={x => { setTo(x.target.value); setPage(1) }} /></label>
        <button type="button" disabled={reviews.isFetching || decision.isPending} onClick={() => void reviews.refetch()}>Refresh matches</button>
      </div>
      {status === 'needs-review' && <p>Shows changed confirmations across all dates. They no longer exclude transactions from totals.</p>}
      {notice && <p role="status" className="transfer-notice">{notice}</p>}
      {decision.error && <p role="alert">{readError(decision.error)}</p>}
      {reviews.isError && <p role="alert">{readError(reviews.error)}</p>}
      {reviews.isLoading && <p>Looking for matching transactions…</p>}
      {reviews.data?.totalCount === 0 && <section className="panel"><h2>{status === 'suggested' ? 'No suggested pairs in this date range' : 'No pairs in this view'}</h2><p>{status === 'suggested' ? 'Try another date range, or import the other account’s transactions. Clear transfers may already be in Matched transfers.' : 'Choose Possible matches to inspect uncertain pairs.'}</p></section>}
      <div className="transfer-list">
        {(reviews.data?.items ?? []).map(pair => (
          <article className="panel transfer-pair" key={`${pair.debit.id}:${pair.credit.id}`}>
            <div className="transfer-pair-heading"><h2>{pair.status === 'confirmed' ? 'Matched transfer' : pair.status === 'needs-review' ? 'Confirmation needs review' : pair.status === 'dismissed' ? 'Dismissed pair' : 'Possible transfer'}</h2>{pair.isAmbiguous && <span className="transfer-ambiguous">Multiple possible matches</span>}</div>
            <div className="transfer-legs"><TransferTransaction leg={pair.debit} label="Money out" /><ArrowRightLeft aria-hidden="true" /><TransferTransaction leg={pair.credit} label="Money in" /></div>
            <Help title="Match details"><p>{pair.explanation}</p></Help>
            {pair.isAmbiguous && <p>At least one transaction has another possible match. Confirm only the correct pair; a transaction can belong to one confirmed transfer.</p>}
            {pair.reviewedAt && <p className="transfer-reviewed">Last reviewed {new Date(pair.reviewedAt).toLocaleString()}</p>}
            <div className="transfer-actions">
              {(pair.status === 'suggested' || pair.status === 'needs-review') && <button type="button" disabled={decision.isPending} onClick={() => review(pair, 'confirm')}>Confirm transfer</button>}
              {pair.status === 'suggested' && <button className="transfer-secondary" type="button" disabled={decision.isPending} onClick={() => review(pair, 'dismiss')}>Not a transfer</button>}
              {pair.status !== 'suggested' && <button className="transfer-secondary" type="button" disabled={decision.isPending} onClick={() => review(pair, 'reset')}>{pair.status === 'dismissed' ? 'Return to suggestions' : 'Undo confirmation'}</button>}
            </div>
          </article>
        ))}
      </div>
      {reviews.data && reviews.data.totalCount > 0 && <div className="transfer-pagination"><span>{reviews.data.totalCount} pairs · Page {page} of {Math.ceil(reviews.data.totalCount / reviews.data.pageSize)}</span><button disabled={page === 1 || reviews.isFetching} onClick={() => setPage(x => x - 1)} type="button">Previous</button><button disabled={page * reviews.data.pageSize >= reviews.data.totalCount || reviews.isFetching} onClick={() => setPage(x => x + 1)} type="button">Next</button></div>}
    </section>
  )
}

function TransferTransaction({ leg, label }: { leg: TransferLeg; label: string }) {
  return <div className="transfer-leg"><span>{label} · {leg.accountName}</span><strong>{exactAmount(leg.amount, leg.currency)}</strong><p>{leg.description}</p><time dateTime={leg.postedAt ?? undefined}>{leg.postedAt ? leg.postedAt.slice(0, 10) : 'No posting date'}</time></div>
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
