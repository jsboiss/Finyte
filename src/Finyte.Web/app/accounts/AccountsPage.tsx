import { Help } from '../shared/Help'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { isAxiosError } from 'axios'
import { useState } from 'react'
import { httpClient } from '../api/httpClient'
import { accountTypeLabel, accountTypes, defaultAnalytics, getAccounts, type Account } from './accountsApi'

type Editor = { account: Account; balance: boolean }

export function AccountsPage() {
  const queryClient = useQueryClient()
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: getAccounts })
  const [editor, setEditor] = useState<Editor | null>(null)
  const [saved, setSaved] = useState('')
  const onSaved = async (account: Account) => {
    setEditor(null)
    setSaved(`${account.name} updated.`)
    await Promise.all(['accounts', 'transactions', 'internal-transfers', 'overview', 'cash-flow', 'budgets', 'pay-cycles'].map(x => queryClient.invalidateQueries({ queryKey: [x] })))
  }

  return (
    <section className="page accounts-page">
      <header className="page-header"><div><h1>Accounts</h1></div><Link to="/imports">Add an account</Link></header>
      <Help title="Account preferences"><p>Names and preferences are shared with your household. Choose which accounts count in combined spending and income. Balances always include every account; viewing an account directly includes its activity.</p></Help>
      {saved && <p role="status">{saved}</p>}
      {accounts.isLoading && <p>Loading accounts…</p>}
      {accounts.isError && <p role="alert">Unable to load accounts. <button type="button" onClick={() => void accounts.refetch()}>Retry</button></p>}
      {accounts.data?.length === 0 && <p>No accounts yet. <Link to="/imports">Create an account for imports</Link> or <Link to="/connections">connect your bank</Link>.</p>}
      <div className="account-list">
        {(accounts.data ?? []).map(account => (
          <article className="panel account-card" key={account.id}>
            <div><h2>{account.name}</h2><p>{account.isProviderManaged ? 'Bank connected' : 'Manual / imports'} · {accountTypeLabel(account.accountType)}{account.accountTypeOverride === null ? ' (default)' : ''}</p></div>
            <div><strong>{formatBalance(account.currentBalance, account.currency)}</strong><p>{account.balanceAsOf ? `Balance updated ${new Date(account.balanceAsOf).toLocaleString()}` : 'Balance update time unavailable'}</p></div>
            <p>{account.includeInAnalytics ? 'Included in combined spending and income' : 'Excluded from combined spending and income'}{account.includeInAnalyticsOverride === null ? ' · Type default' : ' · Family preference'}</p>
            <div className="account-actions">
              <button type="button" onClick={() => { setSaved(''); setEditor({ account, balance: false }) }}>Edit preferences</button>
              {!account.isProviderManaged && <button type="button" onClick={() => { setSaved(''); setEditor({ account, balance: true }) }}>Update balance</button>}
            </div>
            {editor?.account.id === account.id && <AccountEditor key={`${account.id}-${editor.balance}-${editor.account.preferencesVersion}-${editor.account.manualBalanceVersion}`} editor={editor} onCancel={() => setEditor(null)} onSaved={onSaved} onReload={async () => { await accounts.refetch(); setEditor(null) }} />}
          </article>
        ))}
      </div>
    </section>
  )
}

function AccountEditor({ editor, onCancel, onSaved, onReload }: {
  editor: Editor; onCancel: () => void; onSaved: (account: Account) => Promise<void>; onReload: () => Promise<void>
}) {
  const { account, balance: editingBalance } = editor
  const [name, setName] = useState(account.customName ?? '')
  const [type, setType] = useState(account.accountTypeOverride ?? '')
  const [analytics, setAnalytics] = useState(account.includeInAnalyticsOverride === null ? 'default' : account.includeInAnalyticsOverride ? 'include' : 'exclude')
  const [balance, setBalance] = useState(String(account.currentBalance))
  const [available, setAvailable] = useState(account.availableBalance === null ? '' : String(account.availableBalance))
  const effectiveType = type || account.inferredAccountType
  const included = analytics === 'default' ? defaultAnalytics(effectiveType) : analytics === 'include'
  const mutation = useMutation({
    mutationFn: () => httpClient<Account>({
      method: 'PUT', url: `/api/accounts/${account.id}/${editingBalance ? 'balance' : 'preferences'}`,
      data: editingBalance
        ? { currentBalance: balance, availableBalance: available || null, expectedVersion: account.manualBalanceVersion }
        : { customName: name.trim() || null, accountTypeOverride: type || null, includeInAnalyticsOverride: analytics === 'default' ? null : analytics === 'include', expectedVersion: account.preferencesVersion },
    }),
    onSuccess: onSaved,
  })

  return (
    <form className="account-editor" onSubmit={event => { event.preventDefault(); mutation.mutate() }}>
      <h3>{editingBalance ? 'Update manual balance' : 'Edit account preferences'}</h3>
      {editingBalance ? <>
        <p>Enter the balance shown by your bank now, in {account.currency}. This records a balance snapshot; it does not create transactions. OFX imports do not update balances.</p>
        <label>Current balance<input required type="number" step="0.01" value={balance} onChange={event => setBalance(event.target.value)} disabled={mutation.isPending} /></label>
        <label>Available balance (optional)<input type="number" step="0.01" value={available} onChange={event => setAvailable(event.target.value)} disabled={mutation.isPending} /></label>
      </> : <>
        <p>{account.isProviderManaged ? 'Bank name' : 'Original name'}: <strong>{account.originalName}</strong>{account.productName && ` · ${account.productName}`}</p>
        <label>Custom display name<input maxLength={120} value={name} placeholder={account.originalName} onChange={event => setName(event.target.value)} disabled={mutation.isPending} /></label>
        <p>Leave the name empty to use the {account.isProviderManaged ? 'bank' : 'original'} name.</p>
        <label>Account type<select value={type} onChange={event => setType(event.target.value)} disabled={mutation.isPending}>
          <option value="">Use default ({accountTypeLabel(account.inferredAccountType)})</option>
          {accountTypes.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></label>
        <p>The bank may group everyday, savings and offset accounts together. Set the type that matches how you use this account.</p>
        <label>Combined spending and income<select value={analytics} onChange={event => setAnalytics(event.target.value)} disabled={mutation.isPending}>
          <option value="default">Use type default ({defaultAnalytics(effectiveType) ? 'include' : 'exclude'})</option>
          <option value="include">Always include</option><option value="exclude">Always exclude</option>
        </select></label>
        <p role="status">This account will be <strong>{included ? 'included' : 'excluded'}</strong> in combined spending and income. Its balance and individual account view remain available.</p>
      </>}
      <div className="account-actions"><button type="submit" disabled={mutation.isPending}>{mutation.isPending ? 'Saving…' : 'Save changes'}</button><button type="button" disabled={mutation.isPending} onClick={onCancel}>Cancel</button></div>
      {mutation.error && <p role="alert">{errorMessage(mutation.error)} {isAxiosError(mutation.error) && mutation.error.response?.status === 409 && <button type="button" onClick={() => void onReload()}>Discard edits and reload</button>}</p>}
    </form>
  )
}

function formatBalance(balance: number | string, currency: string) {
  return new Intl.NumberFormat('en-AU', { style: 'currency', currency }).format(Number(balance))
}

function errorMessage(error: Error) {
  if (isAxiosError(error)) {
    const data: unknown = error.response?.data
    if (typeof data === 'string') {
      return data
    }
    if (data && typeof data === 'object' && 'detail' in data && typeof data.detail === 'string') {
      return data.detail
    }
  }
  return 'Unable to save changes. Please try again.'
}
