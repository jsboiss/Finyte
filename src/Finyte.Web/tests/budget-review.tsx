// Development-only fixture. No requests reach the API or user database.
import axios, { AxiosError } from 'axios'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Drawer } from '../app/shared/Drawer'
import '../app/index.css'
import '../app/App.css'

const state = new URLSearchParams(location.search).get('state')
let previewAttempts = 0
axios.defaults.adapter = async config => {
  const input = typeof config.data === 'string' ? JSON.parse(config.data) : config.data
  let data: unknown
  if (config.url === '/api/accounts') {
    data = [{ id: 'everyday', name: 'Sample everyday', currency: 'AUD', includeInAnalytics: true }]
  } else if (config.url === '/api/tags') {
    data = [{ id: 'shared', name: 'Shared', color: '#84cc16' }, { id: 'holiday', name: 'Holiday', color: '#38bdf8' }]
  } else if (config.url === '/api/budgets/categories') {
    data = ['Dining', 'Groceries', 'Transport', 'Utilities']
  } else if (config.url === '/api/budgets/preview') {
    if (state === 'error' && previewAttempts++ === 0) { throw new AxiosError('Synthetic preview failure', 'ERR_NETWORK', config) }
    const hasMatches = config.params.date.startsWith('2026-09') && (input.matchMode === 'all' || input.categories.includes('Groceries') || input.tagIds.includes('shared'))
    const items = hasMatches ? [
      { id: '1', description: 'Sample fresh food market', amount: -82.40, postedAt: '2026-09-18', accountName: 'Sample everyday', currency: 'AUD' },
      { id: '2', description: 'Sample neighbourhood grocer', amount: -46.10, postedAt: '2026-09-14', accountName: 'Sample everyday', currency: 'AUD' },
      { id: '3', description: 'Sample pantry shop', amount: -27.50, postedAt: '2026-09-10', accountName: 'Sample everyday', currency: 'AUD' },
    ] : []
    const month = config.params.date.slice(0, 7)
    const end = new Date(Date.UTC(Number(month.slice(0, 4)), Number(month.slice(5)), 0)).toISOString().slice(0, 10)
    data = { from: `${month}-01`, to: end, currency: input.currency, spent: hasMatches ? 156 : 0, transactionCount: items.length, observedThrough: `${month}-22`, excludedCurrencies: [], items }
  } else if (config.url?.startsWith('/api/budgets/') && config.method === 'put') {
    data = { ...input, id: 'sample', version: 1 }
  } else { throw new Error(`Fixture has no response for ${config.url}`) }
  return { data, status: 200, statusText: 'OK', headers: {}, config }
}
const { BudgetEditor } = await import('../app/budgets/BudgetsPage')
const budget = { id: 'sample', name: 'Groceries', limit: 400, currency: 'AUD', frequency: 'monthly', anchorDate: '2026-09-01', matchMode: 'selected', categories: state === 'missing' ? ['Deleted category'] : [], tagIds: state === 'missing' ? ['deleted'] : [], accountScope: 'analytics', accountIds: [], version: 0 }
createRoot(document.getElementById('root')!).render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
  <Drawer title="Budget · synthetic examples" onClose={() => {}}><BudgetEditor budget={budget} onCancel={() => {}} onReload={async () => {}} onSaved={async () => { document.title = 'Synthetic budget saved' }} /></Drawer>
</QueryClientProvider>)
