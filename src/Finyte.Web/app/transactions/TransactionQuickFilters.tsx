import { useState } from 'react'
import type { TransactionFilters } from './transactionSearch'

export function TransactionQuickFilters({ filters, onApply }: { filters: TransactionFilters; onApply: (filters: TransactionFilters) => void }) {
  const [draft, setDraft] = useState(filters)
  const [exact, setExact] = useState(filters.minAmount !== '' && filters.minAmount === filters.maxAmount)
  const [error, setError] = useState('')
  const change = (values: Partial<TransactionFilters>) => { setDraft(x => ({ ...x, ...values })); setError('') }
  const noun = draft.direction === 'debit' ? 'payments' : draft.direction === 'credit' ? 'receipts' : 'amounts'
  return <section className="panel transaction-quick-filters" aria-label="Transaction search">
    <form className="quick-search-row" onSubmit={x => { x.preventDefault(); onApply({ ...filters, search: draft.search.trim() }) }}><label className="filter-field"><span className="visually-hidden">Search transactions</span><input type="search" maxLength={200} placeholder="Search transactions" title="Search merchant, description or reference" value={draft.search} onChange={x => change({ search: x.target.value })} /></label><button type="submit">Search</button></form>
    <details className="quick-filter-options"><summary>Amount &amp; sort</summary><form className="quick-filter-fields" onSubmit={x => {
      x.preventDefault()
      if (draft.minAmount && draft.maxAmount && Number(draft.minAmount) > Number(draft.maxAmount)) { setError('Minimum must be less than or equal to maximum.'); return }
      onApply({ ...draft, search: draft.search.trim() })
      x.currentTarget.closest('details')?.removeAttribute('open')
    }}>
    <label className="filter-field"><span>Money movement</span><select value={draft.direction} onChange={x => change({ direction: x.target.value })}><option value="all">In &amp; out</option><option value="debit">Money out</option><option value="credit">Money in</option></select></label>

    {draft.amountMode === 'signed' ? <div className="quick-search"><p>Signed amount filters are active in Advanced.</p><button className="secondary-button" type="button" onClick={() => change({ amountMode: 'absolute', minAmount: '', maxAmount: '' })}>Use positive amounts</button></div> : <>
      <label className="filter-field"><span>Amount match</span><select value={exact ? 'exact' : 'range'} onChange={x => { setExact(x.target.value === 'exact'); change({ minAmount: '', maxAmount: '' }) }}><option value="range">Range</option><option value="exact">Exact amount</option></select></label>

      <div className="filter-field quick-search"><span>{exact ? 'Exact amount' : 'Amount range'}</span><div className="range-filter"><input aria-label={exact ? 'Exact amount' : 'Minimum amount'} type="number" min="0" step="0.01" placeholder={exact ? 'e.g. 20.00' : 'Min'} value={draft.minAmount} onChange={x => change({ minAmount: x.target.value, ...(exact ? { maxAmount: x.target.value } : {}) })} />{!exact && <input aria-label="Maximum amount" type="number" min="0" step="0.01" placeholder="Max" value={draft.maxAmount} onChange={x => change({ maxAmount: x.target.value })} />}</div></div>
      <small className="quick-search">Positive amounts; inclusive bounds or exact cents. {draft.direction === 'all' ? 'Both directions, including zero.' : draft.direction === 'debit' ? 'Money out only.' : 'Money in only.'} Currencies are not converted.</small>
    </>}
    <label className="filter-field quick-search"><span>Sort transactions</span><select value={draft.sort} onChange={x => change({ sort: x.target.value })}><option value="-date">Newest first</option><option value="date">Oldest first</option><option value="-magnitude">Largest {noun} first</option><option value="magnitude">Smallest {noun} first</option><option value="description">Description: A to Z</option><option value="-description">Description: Z to A</option>{['amount', '-amount'].includes(draft.sort) && <option value={draft.sort}>Signed amount: {draft.sort === 'amount' ? 'lowest' : 'highest'} first (Advanced)</option>}</select></label>
    <div className="quick-actions"><button type="submit">Apply filters</button></div>
    {error && <p role="alert">{error}</p>}
    </form></details>
  </section>
}

export function TransactionFilterChips({ filters, accounts, tags, onApply }: { filters: TransactionFilters; accounts: { id: string; name: string }[]; tags: { id: string; name: string }[]; onApply: (filters: TransactionFilters) => void }) {
  const chips: { label: string; clear: Partial<TransactionFilters> }[] = []
  const add = (label: string, clear: Partial<TransactionFilters>) => chips.push({ label, clear })
  if (filters.search) { add(`Search: ${filters.search}`, { search: '' }) }
  if (filters.direction !== 'all') { add(filters.direction === 'debit' ? 'Money out' : 'Money in', { direction: 'all' }) }
  if (filters.minAmount || filters.maxAmount) { add(`${filters.amountMode === 'signed' ? 'Signed' : 'Amount'}: ${filters.minAmount === filters.maxAmount ? `exactly ${filters.minAmount}` : `${filters.minAmount || 'any'} to ${filters.maxAmount || 'any'} (inclusive)`}`, { minAmount: '', maxAmount: '' }) }
  if (filters.amountMode === 'signed') { add('Signed mode', { amountMode: 'absolute', minAmount: '', maxAmount: '' }) }
  if (filters.accountIds.length > 0) { add(`Accounts: ${filters.accountIds.map(id => accounts.find(x => x.id === id)?.name ?? 'Selected account').join(', ')}`, { accountIds: [] }) }
  if (filters.accountId) { add(`Account: ${accounts.find(x => x.id === filters.accountId)?.name ?? 'Selected account'}`, { accountId: '' }) }
  if (filters.from || filters.to) { add(`Dates: ${filters.from || 'any'} to ${filters.to || 'any'}`, { from: '', to: '' }) }
  if (filters.category) { add(`Category: ${filters.category}`, { category: '' }) }
  if (filters.currency) { add(`Currency: ${filters.currency}`, { currency: '' }) }
  if (filters.untagged) { add('Untagged only', { untagged: false }) }
  for (const tagId of filters.tagIds) { add(`Tag (${filters.tagMatch}): ${tags.find(x => x.id === tagId)?.name ?? 'Selected tag'}`, { tagIds: filters.tagIds.filter(x => x !== tagId), ...(filters.tagIds.length === 1 ? { tagMatch: 'any' as const } : {}) }) }
  if (filters.postedOnly) { add('Posted only', { postedOnly: false }) }
  if (filters.analyticsOnly) { add('Dashboard accounts', { analyticsOnly: false }) }
  if (filters.internalTransfers !== 'include') { add(filters.internalTransfers === 'only' ? 'Transfers only' : 'Transfers excluded', { internalTransfers: 'include' }) }
  if (filters.sort !== '-date') { add(`Sort: ${{ date: 'oldest first', magnitude: 'smallest first', '-magnitude': 'largest first', amount: 'signed lowest first', '-amount': 'signed highest first', description: 'A to Z', '-description': 'Z to A' }[filters.sort] ?? filters.sort}`, { sort: '-date' }) }
  return <div className="transaction-filter-chips" aria-label="Applied filters">{chips.map(x => <button className="secondary-button" type="button" key={x.label} aria-label={`Remove ${x.label}`} onClick={() => onApply({ ...filters, ...x.clear })}>{x.label} ×</button>)}</div>
}
