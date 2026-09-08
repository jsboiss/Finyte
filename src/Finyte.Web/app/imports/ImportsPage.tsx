import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import { Plus, Upload } from 'lucide-react'
import { useState } from 'react'
import { getAccounts } from '../api/generated/finyteApi'
import { httpClient } from '../api/httpClient'

type ImportResult = { importedCount: number; skippedCount: number; totalCount: number }
type ImportRun = ImportResult & {
  id: string
  accountId: string
  fileName: string
  status: string
  error?: string
  startedAt: string
}

export function ImportsPage() {
  const queryClient = useQueryClient()
  const [accountId, setAccountId] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [name, setName] = useState('')
  const [balance, setBalance] = useState('0')
  const [currency, setCurrency] = useState('AUD')
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: () => getAccounts() })
  const imports = useQuery({
    queryKey: ['imports'],
    queryFn: () => httpClient<ImportRun[]>({ method: 'GET', url: '/api/imports' }),
    refetchInterval: x => x.state.data?.some(y => y.status === 'running') ? 3000 : false,
  })
  const upload = useMutation({
    mutationFn: () => {
      if (!file || !accountId) {
        throw new Error('Select an account and an OFX file.')
      }
      const data = new FormData()
      data.append('accountId', accountId)
      data.append('file', file)
      return httpClient<ImportResult>({ method: 'POST', url: '/api/imports/ofx', data })
    },
    onSuccess: async () => {
      await Promise.all(['accounts', 'transactions', 'overview', 'cash-flow', 'tags'].map(x => queryClient.invalidateQueries({ queryKey: [x] })))
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['imports'] }),
  })
  const createAccount = useMutation({
    mutationFn: () => httpClient<{ id: string }>({
      method: 'POST', url: '/api/accounts',
      data: { name: name.trim(), currentBalance: Number(balance), currency },
    }),
    onSuccess: async account => {
      await queryClient.invalidateQueries({ queryKey: ['accounts'] })
      await queryClient.invalidateQueries({ queryKey: ['overview'] })
      setAccountId(account.id)
      setName('')
      upload.reset()
    },
  })

  return (
    <section className="page imports-page">
      <header className="page-header"><div><p>Bank exports</p><h1>Imports</h1></div></header>
      <section className="panel import-panel">
        <div><h2>Import transactions</h2><p>Export one account from your bank as OFX, then select the matching account below. Existing transactions are skipped. Files can be up to 10 MB.</p></div>
        <form className="import-form" onSubmit={x => { x.preventDefault(); upload.mutate() }}>
          <label>Account
            <select required disabled={upload.isPending || accounts.isLoading} value={accountId} onChange={x => { setAccountId(x.target.value); upload.reset() }}>
              <option value="">Select an account</option>
              {(accounts.data ?? []).map(x => <option key={x.id} value={x.id}>{x.name} ({x.currency})</option>)}
            </select>
          </label>
          <label>OFX file
            <input required disabled={upload.isPending} type="file" accept=".ofx,application/x-ofx" onChange={x => { setFile(x.target.files?.[0] ?? null); upload.reset() }} />
          </label>
          <button disabled={!accountId || !file || file.size > 10 * 1024 * 1024 || upload.isPending || createAccount.isPending} type="submit"><Upload aria-hidden="true" />{upload.isPending ? 'Importing…' : 'Import OFX'}</button>
        </form>
        {file && file.size > 10 * 1024 * 1024 && <p role="alert">Choose a file smaller than 10 MB.</p>}
        {accounts.isError && <p role="alert">Unable to load accounts. <button onClick={() => void accounts.refetch()} type="button">Retry</button></p>}
        {accounts.data?.length === 0 && <p>Create your first account below to start importing. No bank connection is needed.</p>}
        {upload.data && <p role="status">Imported {upload.data.importedCount} and skipped {upload.data.skippedCount} existing transactions ({upload.data.totalCount} total).</p>}
        {upload.error && <p role="alert">{errorMessage(upload.error)}</p>}
      </section>
      <section className="panel import-panel">
        <div><h2>Add an account for imports</h2><p>Enter the current balance shown by your bank. Transaction imports do not change this balance.</p></div>
        <form className="import-form" onSubmit={x => { x.preventDefault(); createAccount.mutate() }}>
          <label>Account name<input required maxLength={120} value={name} onChange={x => setName(x.target.value)} placeholder="Everyday account" /></label>
          <label>Current balance<input required type="number" step="0.01" value={balance} onChange={x => setBalance(x.target.value)} /></label>
          <label>Currency<input required pattern="[A-Za-z]{3}" maxLength={3} value={currency} onChange={x => setCurrency(x.target.value.toUpperCase())} /></label>
          <button disabled={!name.trim() || createAccount.isPending || upload.isPending} type="submit"><Plus aria-hidden="true" />{createAccount.isPending ? 'Creating…' : 'Create account'}</button>
        </form>
        {createAccount.error && <p role="alert">{errorMessage(createAccount.error)}</p>}
        {createAccount.isSuccess && <p role="status">Account created and selected for import.</p>}
      </section>
      <section className="panel import-panel">
        <h2>Recent imports</h2>
        {imports.isLoading && <p>Loading import history…</p>}
        {imports.isError && <p role="alert">Unable to load imports. <button onClick={() => void imports.refetch()} type="button">Retry</button></p>}
        {imports.data?.length === 0 && <p>No imports yet.</p>}
        <div className="import-history">
          {(imports.data ?? []).map(x => (
            <article key={x.id}>
              <div><strong>{x.fileName}</strong><span>{accounts.data?.find(y => y.id === x.accountId)?.name ?? 'Account'} · {new Date(x.startedAt).toLocaleString()}</span></div>
              <span className="family-role">{x.status}</span>
              {x.status === 'completed' && <p>{x.importedCount} imported · {x.skippedCount} skipped</p>}
              {x.error && <p role="alert">{x.error}</p>}
            </article>
          ))}
        </div>
      </section>
    </section>
  )
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
    if (error.response?.status === 404) {
      return 'The selected account was not found. Refresh and select an account again.'
    }
  }
  return 'Unable to complete the request. Please try again.'
}
