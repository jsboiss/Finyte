import { Drawer } from '../shared/Drawer'
import { Help } from '../shared/Help'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { isAxiosError } from 'axios'
import { useState } from 'react'
import { httpClient } from '../api/httpClient'
import { exactAmount } from '../shared/formatters'
import { accountTypeLabel, accountTypes, defaultAnalytics, getAccounts, type Account } from './accountsApi'
import { balanceGuidance, balanceState } from './balanceState'

type Editor = { account: Account }

export function AccountsPage() {
  const queryClient = useQueryClient()
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: getAccounts })
  const [editor, setEditor] = useState<Editor | null>(null)
  const [saved, setSaved] = useState('')
  const [search, setSearch] = useState('')
  const onSaved = async (account: Account) => {
    setEditor(null)
    setSaved(`${account.name} updated.`)
    await Promise.all(['accounts', 'transactions', 'internal-transfers', 'overview', 'cash-flow', 'budgets', 'pay-cycles'].map(x => queryClient.invalidateQueries({ queryKey: [x] })))
  }

  return (
    <section className="page accounts-page">
      <header className="page-header"><div><h1>Accounts</h1><Help title="Account preferences"><p>Names and preferences are shared with your household. Choose which accounts count in combined spending and income. Balances always include every account; viewing an account directly includes its activity.</p></Help></div><Link to="/imports">Add an account</Link></header>

      {saved && <p role="status">{saved}</p>}
      {accounts.isLoading && <p>Loading accounts…</p>}
      {accounts.isError && <p role="alert">Unable to load accounts. <button type="button" onClick={() => void accounts.refetch()}>Retry</button></p>}
      {accounts.data?.length === 0 && <p>No accounts yet. <Link to="/imports">Create an account for imports</Link> or <Link to="/connections">connect your bank</Link>.</p>}
      {(accounts.data?.length ?? 0) > 5 && <input aria-label="Search accounts" type="search" placeholder="Search accounts" value={search} onChange={event => setSearch(event.target.value)} />}
      <div className="account-list">
        {(accounts.data ?? []).filter(account => account.name.toLowerCase().includes(search.toLowerCase())).map(account => (
          <article className="panel account-card" key={account.id}>
            <div className="account-identity"><h2>{account.name}</h2><span>{account.isProviderManaged ? 'Connected' : 'Imported'} · {accountTypeLabel(account.accountType)}</span>{!account.includeInAnalytics && <small>Excluded from combined spending</small>}</div>
            <AccountBalance account={account} />
            <div className="account-actions">
              <button type="button" onClick={() => { setSaved(''); setEditor({ account }) }}>Edit preferences</button>
            </div>

          </article>
        ))}
      </div>
            {editor && <Drawer title={`Account preferences · ${editor.account.name}`} onClose={() => setEditor(null)}><AccountEditor key={`${editor.account.id}-${editor.account.preferencesVersion}`} editor={editor} onCancel={() => setEditor(null)} onSaved={onSaved} onReload={async () => { await accounts.refetch(); setEditor(null) }} /></Drawer>}
    </section>
  )
}

function AccountEditor({ editor, onCancel, onSaved, onReload }: {
  editor: Editor; onCancel: () => void; onSaved: (account: Account) => Promise<void>; onReload: () => Promise<void>
}) {
  const { account } = editor
  const [name, setName] = useState(account.customName ?? '')
  const [type, setType] = useState(account.accountTypeOverride ?? '')
  const [analytics, setAnalytics] = useState(account.includeInAnalyticsOverride === null ? 'default' : account.includeInAnalyticsOverride ? 'include' : 'exclude')
  const effectiveType = type || account.inferredAccountType
  const included = analytics === 'default' ? defaultAnalytics(effectiveType) : analytics === 'include'
  const mutation = useMutation({
    mutationFn: () => httpClient<Account>({
      method: 'PUT', url: `/api/accounts/${account.id}/preferences`,
      data: { customName: name.trim() || null, accountTypeOverride: type || null, includeInAnalyticsOverride: analytics === 'default' ? null : analytics === 'include', expectedVersion: account.preferencesVersion },
    }),
    onSuccess: onSaved,
  })

  return (
    <form className="account-editor" onSubmit={event => { event.preventDefault(); mutation.mutate() }}>


        <p>{account.isProviderManaged ? 'Bank name' : 'Original name'}: <strong>{account.originalName}</strong>{account.productName && ` · ${account.productName}`}</p>
        <label>Custom display name<input maxLength={120} value={name} placeholder={account.originalName} onChange={event => setName(event.target.value)} disabled={mutation.isPending} /></label>

        <label>Account type<select value={type} onChange={event => setType(event.target.value)} disabled={mutation.isPending}>
          <option value="">Use default ({accountTypeLabel(account.inferredAccountType)})</option>
          {accountTypes.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></label>

        <label>Combined spending and income<select value={analytics} onChange={event => setAnalytics(event.target.value)} disabled={mutation.isPending}>
          <option value="default">Use type default ({defaultAnalytics(effectiveType) ? 'include' : 'exclude'})</option>
          <option value="include">Always include</option><option value="exclude">Always exclude</option>
        </select></label>
        <p role="status"><strong>{included ? 'Included' : 'Excluded'}</strong> in combined spending and income.</p>

      <div className="account-actions"><button type="submit" disabled={mutation.isPending}>{mutation.isPending ? 'Saving…' : 'Save changes'}</button><button type="button" disabled={mutation.isPending} onClick={onCancel}>Cancel</button></div>
      {mutation.error && <p role="alert">{errorMessage(mutation.error)} {isAxiosError(mutation.error) && mutation.error.response?.status === 409 && <button type="button" onClick={() => void onReload()}>Discard edits and reload</button>}</p>}
    </form>
  )
}

function AccountBalance({ account }: { account: Account }) {
  const state = balanceState(account)
  const guidance = balanceGuidance(account, state)

  return (
    <div className="account-balance">
      {/* A reported balance of zero is a real balance, so it is shown as an amount and never as unavailable. */}
      <strong>{state.kind === 'missing' ? 'Balance unavailable' : formatBalance(account.currentBalance, account.currency)}</strong>
      {state.kind !== 'missing' && <span>Updated {new Date(state.asOf).toLocaleDateString()}</span>}
      {guidance && <small className={state.kind === 'missing' ? 'balance-missing' : 'balance-stale'}>
        {guidance.text}{guidance.to && <> <Link to={guidance.to}>{guidance.action}</Link></>}
      </small>}
    </div>
  )
}

function formatBalance(balance: number | string, currency: string) {
  return exactAmount(Number(balance), currency)
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
