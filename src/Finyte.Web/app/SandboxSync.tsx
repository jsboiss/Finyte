import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import axios from 'axios'
import { httpClient } from './api/httpClient'

type SandboxStatus = {
  configured: boolean
  isRunning: boolean
  accountCount: number
  transactionCount: number
  runs: { dataset: string; status: string; error: string | null; completedAt: string | null }[]
}

export function SandboxSync() {
  const [confirmReset, setConfirmReset] = useState(false)
  const client = useQueryClient()
  const wasRunning = useRef(false)
  const status = useQuery({
    queryKey: ['fiskil-sandbox'],
    queryFn: () => httpClient<SandboxStatus>({ url: '/api/development/fiskil-sandbox' }),
    retry: false,
    refetchInterval: query => query.state.data?.isRunning ? 2000 : false,
  })
  const sync = useMutation({
    mutationFn: () => httpClient<void>({ method: 'POST', url: '/api/development/fiskil-sandbox/sync', data: { confirmReset: true } }),
    onSuccess: async () => {
      setConfirmReset(false)
      await client.invalidateQueries()
    },
  })
  const running = status.data?.isRunning ?? false
  useEffect(() => {
    if (wasRunning.current && !running) {
      void client.invalidateQueries({ predicate: query => query.queryKey[0] !== 'fiskil-sandbox' })
    }
    wasRunning.current = running
  }, [running, client])

  // Production APIs do not register this development route.
  if (axios.isAxiosError(status.error) && status.error.response?.status === 404) { return null }

  const error = axios.isAxiosError(sync.error)
    ? sync.error.response?.data?.detail ?? sync.error.message
    : sync.error?.message
  return <section className="panel">
    <h2>Fiskil sandbox · development</h2>
    <p>Clear this household’s financial data, then import fresh transactions from Fiskil sandbox through the normal sync process.</p>
    <p>Tags, tagging rules and budgets are retained. Accounts referenced by budgets are kept with their balances cleared.</p>
    {status.isPending && <p>Checking sandbox setup…</p>}
    {status.error && <p role="alert">Unable to check sandbox sync status.</p>}
    {status.data && !status.data.configured && <p>Configure Fiskil sandbox credentials and consent on the development server to enable syncing.</p>}
    <button type="button" disabled={!status.data?.configured || running || sync.isPending} onClick={() => setConfirmReset(true)}>
      {sync.isPending ? 'Resetting and requesting sync…' : running ? 'Syncing sandbox…' : 'Reset data and sync sandbox'}
    </button>
    {confirmReset && <div role="alertdialog" aria-label="Reset financial data" aria-describedby="sandbox-reset-description">
      <p id="sandbox-reset-description">This will delete this household’s transactions, statement import history, transfer matches, recurring-payment setup, pay-cycle profiles and cached reports. Tags, tagging rules and budgets will stay. If the sync fails, the cleared data will remain empty until you retry.</p>
      <button type="button" disabled={sync.isPending || running} onClick={() => sync.mutate()}>Clear financial data and sync</button>
      <button type="button" disabled={sync.isPending} onClick={() => setConfirmReset(false)}>Cancel</button>
    </div>}
    {error && <p role="alert">{error}</p>}
    {status.data && <div aria-live="polite">
      {status.data.runs.length > 0 && <>
        <p>{status.data.accountCount} sandbox accounts · {status.data.transactionCount} transactions stored</p>
        <ul>{status.data.runs.map(x => <li key={x.dataset}>{x.dataset}: {x.status}{x.error && <span role="alert"> — {x.error}</span>}</li>)}</ul>
        {running && <p>The background worker is processing this sync. You can leave this page and return to check progress.</p>}
      </>}
    </div>}
  </section>
}
