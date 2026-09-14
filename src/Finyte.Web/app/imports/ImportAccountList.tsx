import { useState } from 'react'
import { Link } from '@tanstack/react-router'

type ImportAccount = { id: string; name: string; currency: string }
const pageSize = 5

export function ImportAccountList({ accounts, selectedId, disabled, onSelect }: {
  accounts: ImportAccount[]; selectedId: string; disabled: boolean; onSelect: (id: string) => void
}) {
  const [search, setSearch] = useState('')
  const [requestedPage, setRequestedPage] = useState(1)
  const filtered = accounts.filter(account => `${account.name} ${account.currency}`.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()))
  const pages = Math.max(1, Math.ceil(filtered.length / pageSize))
  const page = Math.min(requestedPage, pages)
  const start = (page - 1) * pageSize
  return <section className="panel import-panel import-accounts-panel" aria-label="Your accounts">
    <div className="import-accounts-heading"><h2>Your accounts <span>({accounts.length})</span></h2><Link to="/accounts">Manage</Link></div>
    {accounts.length > pageSize && <input aria-label="Search import accounts" type="search" placeholder="Search accounts" value={search} onChange={event => { setSearch(event.target.value); setRequestedPage(1) }} />}
    <ul className="import-account-options">
      {filtered.slice(start, start + pageSize).map(account => <li key={account.id}><button type="button" aria-pressed={selectedId === account.id} disabled={disabled} onClick={() => onSelect(account.id)} title={`${account.name} (${account.currency})`}>
        <span className="import-account-name">{account.name}</span><span className="import-account-currency">{account.currency}</span><span className="import-account-selection" aria-hidden="true">{selectedId === account.id ? '✓' : ''}</span>
      </button></li>)}
    </ul>
    {accounts.length === 0 && <p>Add an account to start importing.</p>}
    {accounts.length > 0 && filtered.length === 0 && <p role="status">No matching accounts.</p>}
    {filtered.length > pageSize && <nav className="import-account-pagination" aria-label="Account pages"><span role="status">{start + 1}–{Math.min(start + pageSize, filtered.length)} of {filtered.length}</span><button type="button" aria-label="Previous account page" disabled={page === 1} onClick={() => setRequestedPage(page - 1)}>‹</button><button type="button" aria-label="Next account page" disabled={page === pages} onClick={() => setRequestedPage(page + 1)}>›</button></nav>}
  </section>
}
