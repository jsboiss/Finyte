import { useState } from 'react'
import type { TransactionTag } from './types'
import type { TransactionFilters } from './transactionSearch'

type TransactionFilterFormProps = {
  filters: TransactionFilters
  accounts: { id: string; name: string; currency: string }[]
  tags: TransactionTag[]
  onApply: (filters: TransactionFilters) => void
}

export function TransactionFilterForm({ filters, accounts, tags, onApply }: TransactionFilterFormProps) {
  const [draft, setDraft] = useState(filters)
  const [error, setError] = useState('')
  const currencies = [...new Set(accounts.map(x => x.currency))].sort()
  const change = <T extends keyof TransactionFilters>(key: T, value: TransactionFilters[T]) => {
    setDraft(x => ({ ...x, [key]: value }))
    setError('')
  }

  return (
    <form className="panel transaction-filters-panel" onSubmit={event => {
      event.preventDefault()
      if (draft.from && draft.to && draft.from > draft.to) {
        setError('Start date must be on or before end date.')
        return
      }
      if (draft.minAmount && draft.maxAmount && Number(draft.minAmount) > Number(draft.maxAmount)) {
        setError('Minimum amount must be less than or equal to maximum amount.')
        return
      }
      onApply({ ...draft, search: draft.search.trim(), category: draft.category.trim() })
    }}>
      <label className="filter-field"><span>Money movement</span><select value={draft.direction} onChange={x => change('direction', x.target.value)}><option value="all">Money in and out</option><option value="debit">Money out only</option><option value="credit">Money in only</option></select></label>
      <label className="filter-field"><span>Confirmed internal transfers</span><select value={draft.internalTransfers} onChange={x => change('internalTransfers', x.target.value)}><option value="include">Include</option><option value="exclude">Exclude</option><option value="only">Only internal transfers</option></select></label>
      <label className="transaction-filter-checkbox"><input type="checkbox" checked={draft.postedOnly} onChange={x => change('postedOnly', x.target.checked)} /><span>Posted transactions only</span></label>
      <label className="transaction-filter-checkbox"><input type="checkbox" checked={draft.analyticsOnly} onChange={x => change('analyticsOnly', x.target.checked)} /><span>Use dashboard account preferences (when all accounts are selected)</span></label>
      <label className="filter-field date-filter-field">
        <span>Date range</span>
        <div className="range-filter">
          <input aria-label="From date" type="date" value={draft.from} onChange={x => change('from', x.target.value)} />
          <input aria-label="To date" type="date" value={draft.to} onChange={x => change('to', x.target.value)} />
        </div>
      </label>
      <label className="filter-field">
        <span>Account</span>
        <select value={draft.accountId} onChange={x => change('accountId', x.target.value)}>
          <option value="">All accounts</option>
          {accounts.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
        </select>
      </label>
      <label className="filter-field">
        <span>Search</span>
        <input maxLength={200} placeholder="Description, merchant or reference" value={draft.search} onChange={x => change('search', x.target.value)} />
      </label>
      <label className="filter-field">
        <span>Category</span>
        <input maxLength={200} placeholder="Search categories" value={draft.category} onChange={x => change('category', x.target.value)} />
      </label>
      <label className="filter-field amount-filter-field">
        <span>Amount range</span>
        <div className="range-filter">
          <input aria-label="Minimum amount" placeholder="Min, e.g. -100" type="number" step="0.01" value={draft.minAmount} onChange={x => change('minAmount', x.target.value)} />
          <input aria-label="Maximum amount" placeholder="Max, e.g. -20" type="number" step="0.01" value={draft.maxAmount} onChange={x => change('maxAmount', x.target.value)} />
        </div>
        <small>Use negative amounts for money out and positive amounts for money in.</small>
      </label>
      <label className="filter-field">
        <span>Currency</span>
        <select value={draft.currency} onChange={x => change('currency', x.target.value)}>
          <option value="">All currencies (no conversion)</option>
          {currencies.map(x => <option key={x} value={x}>{x}</option>)}
        </select>
      </label>
      <label className="filter-field">
        <span>Sort</span>
        <select value={draft.sort} onChange={x => change('sort', x.target.value)}>
          <option value="-date">Newest first</option>
          <option value="date">Oldest first</option>
          <option value="amount">Amount: lowest first</option>
          <option value="-amount">Amount: highest first</option>
          <option value="description">Description: A to Z</option>
          <option value="-description">Description: Z to A</option>
        </select>
      </label>
      <fieldset className="transaction-tag-filters">
        <legend>Tags</legend>
        <label className="transaction-filter-checkbox">
          <input type="checkbox" checked={draft.untagged} onChange={x => setDraft(y => ({ ...y, untagged: x.target.checked, tagIds: [] }))} />
          <span>Untagged only</span>
        </label>
        <label className="filter-field">
          <span>Match selected tags</span>
          <select disabled={draft.untagged} value={draft.tagMatch} onChange={x => change('tagMatch', x.target.value as 'any' | 'all')}>
            <option value="any">Any selected tag</option>
            <option value="all">Every selected tag</option>
          </select>
        </label>
        <div className="transaction-tag-filter-options">
          {tags.map(x => (
            <label className="transaction-filter-checkbox" key={x.id}>
              <input type="checkbox" disabled={draft.untagged} checked={draft.tagIds.includes(x.id)} onChange={y => change('tagIds', y.target.checked ? [...draft.tagIds, x.id] : draft.tagIds.filter(z => z !== x.id))} />
              <span>{x.name}</span>
            </label>
          ))}
          {tags.length === 0 && <span className="empty-inline">No tags created yet.</span>}
        </div>
      </fieldset>
      <div className="transaction-filter-submit">
        {error && <p role="alert">{error}</p>}
        <button type="submit">Apply filters</button>
      </div>
    </form>
  )
}
