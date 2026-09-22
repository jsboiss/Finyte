// Local Vite-only visual fixture: real components, synthetic data, no API/database access.
import { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { TransactionQuickFilters, TransactionFilterChips } from '../app/transactions/TransactionQuickFilters'
import { TransactionFilterForm } from '../app/transactions/TransactionFilters'
import { TransactionCardList } from '../app/transactions/TransactionCardList'
import { Drawer } from '../app/shared/Drawer'
import { Menu, Tags, X } from '../app/shared/Icons'
import { defaultTransactionFilters, type TransactionFilters } from '../app/transactions/transactionSearch'
import type { Transaction } from '../app/transactions/types'
import '../app/index.css'
import '../app/App.css'
import '../app/shared/ux.css'

const accounts = [{ id: 'sample', name: 'Everyday · sample', currency: 'AUD' }]
const samples: Transaction[] = [
  { id: '1', description: 'Market groceries', amountMinorUnits: -10000 },
  { id: '2', description: 'Neighbourhood café', amountMinorUnits: -4500 },
  { id: '3', description: 'Bookshop', amountMinorUnits: -2000 },
  { id: '4', description: 'Bookshop refund', amountMinorUnits: 2000 },
].map(x => ({ ...x, accountId: 'sample', accountDisplayName: 'Everyday · sample', postedDate: '2026-09-22', merchantName: null, category: '', currency: 'AUD', tags: [] }))

export function MobileReview() {
  const [filters, setFilters] = useState<TransactionFilters>({ ...defaultTransactionFilters, direction: 'debit', minAmount: '20', maxAmount: '100', sort: '-magnitude' })
  const [advanced, setAdvanced] = useState(false)
  const transactions = samples.filter(x => {
    const amount = (filters.amountMode === 'absolute' ? Math.abs(x.amountMinorUnits) : x.amountMinorUnits) / 100
    return (!filters.search || x.description.toLowerCase().includes(filters.search.toLowerCase()))
      && (filters.direction === 'all' || (filters.direction === 'debit' ? x.amountMinorUnits < 0 : x.amountMinorUnits > 0))
      && (!filters.minAmount || amount >= Number(filters.minAmount)) && (!filters.maxAmount || amount <= Number(filters.maxAmount))
  }).sort((x, y) => filters.sort === '-magnitude' ? Math.abs(y.amountMinorUnits) - Math.abs(x.amountMinorUnits) : filters.sort === 'magnitude' ? Math.abs(x.amountMinorUnits) - Math.abs(y.amountMinorUnits) : 0)
  return <div className="app-shell">
    <header className="mobile-header"><div className="brand"><span className="logo-placeholder" /><h1>Transactions</h1></div><button className="mobile-menu-button" aria-label="Sample navigation"><Menu className="mobile-menu-icon" size={24} /></button></header>
    <main><section className="page transactions-page">
      <header className="page-header transactions-header"><div><h1>Transactions</h1></div><div className="transactions-actions"><button className="secondary-button" type="button"><Tags />Tags</button><button className="secondary-button" onClick={() => setAdvanced(true)}>Advanced</button><button className="secondary-button" aria-label="Reset all filters" onClick={() => setFilters(defaultTransactionFilters)}><X />Reset</button></div></header>
      <small>Sample data · mobile review</small>
      <TransactionQuickFilters key={JSON.stringify(filters)} filters={filters} onApply={setFilters} />
      <TransactionFilterChips filters={filters} accounts={accounts} tags={[]} onApply={setFilters} />
      <TransactionCardList transactions={transactions} emptyMessage="No transactions match the current filters." isLoading={false} renderTags={() => <span>Sample transaction</span>} />
      <p>{transactions.length} transactions</p>
      {advanced && <Drawer title="Advanced filters" onClose={() => setAdvanced(false)}><TransactionFilterForm filters={filters} accounts={accounts} tags={[]} onApply={x => { setFilters(x); setAdvanced(false) }} /></Drawer>}
    </section></main>
  </div>
}

createRoot(document.getElementById('root')!).render(<MobileReview />)
