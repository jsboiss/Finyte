import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { httpClient } from '../api/httpClient'
import { exactCurrency } from '../shared/formatters'
import { Help } from '../shared/Help'
import type { TransactionTag } from '../transactions/types'
import { acceptTagSuggestions, getTagSuggestions, starterTagNames, type AcceptItem, type MerchantSuggestion, type TagSuggestionGroup } from './tagSuggestionsApi'
import './TagSuggestions.css'

export function TagSuggestionsPage() {
  const client = useQueryClient()
  const [notice, setNotice] = useState('')
  const [mode, setMode] = useState<'rule' | 'once'>('rule')
  const suggestions = useQuery({ queryKey: ['tag-suggestions'], queryFn: getTagSuggestions })
  const tags = useQuery({ queryKey: ['tags'], queryFn: () => httpClient<TransactionTag[]>({ url: '/api/tags' }) })
  const accept = useMutation({
    mutationFn: (items: AcceptItem[]) => acceptTagSuggestions(items.map(x => ({ ...x, mode }))),
    onSuccess: async (result, items) => {
      const added = result.createdTags > 0 ? ` and added ${result.createdTags} ${result.createdTags === 1 ? 'tag' : 'tags'}` : ''
      setNotice(mode === 'once'
        ? `Tagged ${result.taggedTransactions} existing ${result.taggedTransactions === 1 ? 'payment' : 'payments'}${added}. Future payments are not tagged automatically.`
        : `Tagged ${items.length} ${items.length === 1 ? 'merchant' : 'merchants'}${added}. Past and future payments now use these rules.`)
      await Promise.all(['tag-suggestions', 'tags', 'merchant-tags', 'transactions', 'overview', 'budgets'].map(key => client.invalidateQueries({ queryKey: [key] })))
    },
  })
  const tagNames = [...new Set([...(tags.data ?? []).map(x => x.name), ...starterTagNames])].sort((a, b) => a.localeCompare(b))
  const data = suggestions.data

  return <section className="page tag-suggestions-page">
    <header className="page-header"><div className="page-title"><h1>Tag suggestions</h1><Help title="How tag suggestions work"><p>Finyte suggests a tag for merchants you have not tagged yet, using your bank's categories where available and common merchant names otherwise. You choose whether accepting creates a rule for future payments or only tags existing ones. Your manual tags and removed tags are kept.</p></Help></div><Link to="/transactions">Back to transactions</Link></header>
    {notice && <p role="status" className="tag-suggestions-notice">{notice}</p>}
    {suggestions.isLoading && <p role="status">Looking for suggestions…</p>}
    {suggestions.isError && <p role="alert">Unable to load tag suggestions. <button type="button" onClick={() => void suggestions.refetch()}>Retry</button></p>}
    {accept.isError && <p role="alert">Those suggestions could not be saved. Nothing was changed.</p>}
    {data && <>
      <fieldset className="panel tag-suggestion-mode"><legend>When I accept a suggestion</legend>
        <label><input type="radio" name="tag-mode" checked={mode === 'rule'} onChange={() => setMode('rule')} />Tag existing and future payments (creates a rule)</label>
        <label><input type="radio" name="tag-mode" checked={mode === 'once'} onChange={() => setMode('once')} />Only tag existing payments</label>
      </fieldset>
      {data.coverage.map(x => <Coverage key={x.currency} tagged={x.taggedMinorUnits} total={x.totalMinorUnits} currency={x.currency} />)}
      {data.groups.length === 0 && data.needsTag.length === 0 && <section className="panel"><h2>Everything is tagged</h2><p>New merchants will appear here after your next import.</p></section>}
      {data.groups.map(group => <SuggestionGroup key={group.tagName} group={group} tagNames={tagNames} busy={accept.isPending} onAccept={items => accept.mutate(items)} />)}
      {data.needsTag.length > 0 && <section className="panel tag-suggestion-group">
        <div className="tag-suggestion-heading"><div><h2>Needs a tag</h2><p className="tag-suggestion-muted">{data.needsTag.length} merchants without a suggestion</p></div></div>
        <ul className="tag-suggestion-merchants">{data.needsTag.map(merchant => <MerchantRow key={merchant.ruleMerchantName} merchant={merchant} tagNames={tagNames} busy={accept.isPending} onAccept={items => accept.mutate(items)} />)}</ul>
      </section>}
    </>}
  </section>
}

function Coverage({ tagged, total, currency }: { tagged: number; total: number; currency: string }) {
  const percent = total === 0 ? 100 : Math.round(tagged / total * 100)
  return <section className="panel tag-coverage" aria-label={`${currency} tag coverage`}>
    <div className="tag-suggestion-heading"><strong>{percent}% of spending tagged</strong><span className="tag-suggestion-muted">{exactCurrency(tagged, currency)} of {exactCurrency(total, currency)} · last 12 months</span></div>
    <div className="tag-coverage-bar" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}><div style={{ width: `${percent}%` }} /></div>
  </section>
}

function SuggestionGroup({ group, tagNames, busy, onAccept }: { group: TagSuggestionGroup; tagNames: string[]; busy: boolean; onAccept: (items: AcceptItem[]) => void }) {
  const [open, setOpen] = useState(false)
  const spend = group.merchants.reduce((sum, x) => sum + x.spendMinorUnits, 0)
  const tag = (merchant: MerchantSuggestion): AcceptItem => group.tagId ? { merchantName: merchant.ruleMerchantName, tagId: group.tagId } : { merchantName: merchant.ruleMerchantName, tagName: group.tagName }
  return <section className="panel tag-suggestion-group">
    <div className="tag-suggestion-heading">
      <div><h2><span className="tag-suggestion-swatch" style={{ backgroundColor: group.color }} />{group.tagName}</h2>
        <p className="tag-suggestion-muted">{group.merchants.length} {group.merchants.length === 1 ? 'merchant' : 'merchants'} · {exactCurrency(spend, group.merchants[0]?.currency ?? 'AUD')} · {group.merchants.slice(0, 4).map(x => x.ruleMerchantName).join(', ')}{group.merchants.length > 4 ? '…' : ''}</p></div>
      <div className="tag-suggestion-actions"><button type="button" disabled={busy} onClick={() => onAccept(group.merchants.map(tag))}>Accept all</button><button type="button" className="secondary" onClick={() => setOpen(!open)} aria-expanded={open}>{open ? 'Hide' : 'Review'}</button></div>
    </div>
    {open && <ul className="tag-suggestion-merchants">{group.merchants.map(merchant => <MerchantRow key={merchant.ruleMerchantName} merchant={merchant} suggested={group.tagName} tagNames={tagNames} busy={busy} onAccept={onAccept} />)}</ul>}
  </section>
}

function MerchantRow({ merchant, suggested, tagNames, busy, onAccept }: { merchant: MerchantSuggestion; suggested?: string; tagNames: string[]; busy: boolean; onAccept: (items: AcceptItem[]) => void }) {
  const [tagName, setTagName] = useState(suggested ?? '')
  return <li className="tag-suggestion-merchant">
    <div className="tag-suggestion-merchant-name"><strong>{merchant.ruleMerchantName}</strong>
      <span className="tag-suggestion-muted">{merchant.transactionCount} {merchant.transactionCount === 1 ? 'payment' : 'payments'} · {exactCurrency(merchant.spendMinorUnits, merchant.currency)}{merchant.reason ? ` · ${merchant.reason}` : ''}</span>
      {merchant.examples.length > 1 && <span className="tag-suggestion-muted">Includes {merchant.examples.join(', ')}</span>}</div>
    <label className="tag-suggestion-select"><select aria-label={`Tag for ${merchant.ruleMerchantName}`} value={tagName} onChange={event => setTagName(event.target.value)}><option value="">Choose a tag</option>{tagNames.map(x => <option key={x} value={x}>{x}</option>)}</select></label>
    <button type="button" disabled={busy || !tagName} onClick={() => onAccept([{ merchantName: merchant.ruleMerchantName, tagName }])}>Accept</button>
  </li>
}
