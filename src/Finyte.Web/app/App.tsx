import { SignIn, UserButton, useAuth } from '@clerk/react'
import { keepPreviousData, QueryClient, QueryClientProvider, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createRootRoute, createRoute, createRouter, Link, Outlet, RouterProvider } from '@tanstack/react-router'
import { Activity, Banknote, CalendarClock, Home, RefreshCcw, Settings, WalletCards } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { getAccounts, getAppStatus, type AccountResponse } from './api/generated/finyteApi'
import { httpClient } from './api/httpClient'
import { AuthTokenProvider } from './auth/AuthTokenProvider'
import './App.css'

type OverviewResponse = {
  scope: {
    accountId: string | null
    label: string
  }
  monthKey: string
  currency: string
  accountBalanceMinorUnits: number
  currentMonthSpendMinorUnits: number
  averageDailySpendMinorUnits: number
  cashFlowRace: {
    incomeMinorUnits: number
    expenseMinorUnits: number
    netMinorUnits: number
  }
  dailyCashFlow: {
    date: string
    day: number
    incomeMinorUnits: number
    expenseMinorUnits: number
  }[]
  monthlySpendByTag: {
    tagId: string | null
    name: string
    color: string
    amountMinorUnits: number
    percentage: number
  }[]
  freshness: {
    calculatedAt: string
    sourceWatermark: string | null
    isRefreshing: boolean
  }
}

type OverviewAccountOption = {
  id: string
  label: string
}

const allAccountsValue = 'all'
const queryClient = new QueryClient()
const devAuthEnabled = import.meta.env.VITE_DEV_AUTH === 'true'

function DashboardShell() {
  if (devAuthEnabled) {
    return <SignedInShell />
  }

  return <ClerkDashboardShell />
}

function ClerkDashboardShell() {
  const { isLoaded, isSignedIn } = useAuth()

  if (!isLoaded) {
    return (
      <div className="auth-page">
        <div className="auth-loading">Loading</div>
      </div>
    )
  }

  if (!isSignedIn) {
    return <SignInPage />
  }

  return <SignedInShell />
}

function SignedInShell() {
  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <Banknote aria-hidden="true" />
          <span>Finyte</span>
        </div>
        <nav>
          <Link to="/" activeProps={{ className: 'active' }}>
            <Home aria-hidden="true" />
            Dashboard
          </Link>
          <Link to="/connections" activeProps={{ className: 'active' }}>
            <Activity aria-hidden="true" />
            Connections
          </Link>
          <Link to="/settings" activeProps={{ className: 'active' }}>
            <Settings aria-hidden="true" />
            Settings
          </Link>
        </nav>
        <AuthControls />
      </aside>
      <main>
        <Outlet />
      </main>
    </div>
  )
}

function AuthControls() {
  if (devAuthEnabled) {
    return (
      <div className="auth-panel dev-auth-panel">
        <span>Dev User</span>
      </div>
    )
  }

  return (
    <div className="auth-panel">
      <UserButton />
    </div>
  )
}

function SignInPage() {
  return (
    <main className="auth-page">
      <SignIn />
    </main>
  )
}

function DashboardPage() {
  const [selectedAccountId, setSelectedAccountId] = useState(allAccountsValue)
  const queryClient = useQueryClient()
  const statusQuery = useQuery({
    queryKey: ['app-status'],
    queryFn: () => getAppStatus(),
    staleTime: 60_000,
  })
  const accountsQuery = useQuery({
    queryKey: ['accounts'],
    queryFn: () => getAccounts(),
    staleTime: 60_000,
  })
  const accountId = selectedAccountId === allAccountsValue ? null : selectedAccountId
  const overviewQuery = useQuery({
    queryKey: getOverviewQueryKey(accountId),
    queryFn: () => getOverview(accountId),
    placeholderData: keepPreviousData,
    staleTime: 60_000,
  })
  const refreshOverviewMutation = useMutation({
    mutationFn: () => refreshOverview(accountId),
    onSuccess: x => {
      queryClient.setQueryData(getOverviewQueryKey(accountId), x)
    },
  })
  const accountOptions = useMemo<OverviewAccountOption[]>(() => [
    { id: allAccountsValue, label: 'All accounts' },
    ...(accountsQuery.data ?? []).map(x => ({ id: x.id, label: x.name })),
  ], [accountsQuery.data])

  useEffect(() => {
    if (!accountsQuery.data) {
      return
    }

    const ids = [null, ...accountsQuery.data.map(x => x.id)]
    for (const id of ids) {
      queryClient.prefetchQuery({
        queryKey: getOverviewQueryKey(id),
        queryFn: () => getOverview(id),
        staleTime: 60_000,
      })
    }
  }, [accountsQuery.data, queryClient])

  const overview = overviewQuery.data ?? createEmptyOverview(accountId, selectedAccountId, accountsQuery.data)
  const isLoading = overviewQuery.isLoading || accountsQuery.isLoading

  return (
    <section className="page">
      <header className="page-header overview-header">
        <div>
          <p>Household dashboard</p>
          <h1>Financial overview</h1>
        </div>
        <div className="overview-actions">
          <label>
            <span>Account</span>
            <select value={selectedAccountId} onChange={x => setSelectedAccountId(x.target.value)}>
              {accountOptions.map(x => <option key={x.id} value={x.id}>{x.label}</option>)}
            </select>
          </label>
          <button
            aria-label="Refresh overview"
            className="icon-button"
            disabled={overviewQuery.isFetching || refreshOverviewMutation.isPending}
            onClick={() => refreshOverviewMutation.mutate()}
            title="Refresh overview"
            type="button"
          >
            <RefreshCcw aria-hidden="true" />
          </button>
        </div>
      </header>

      <div className="overview-status-row">
        <div className="status-pill">
          <CalendarClock aria-hidden="true" />
          {statusQuery.data?.databaseAvailable ? 'API connected' : 'Waiting for API'}
        </div>
        <div className={overviewQuery.isFetching || refreshOverviewMutation.isPending ? 'freshness is-refreshing' : 'freshness'}>
          {isLoading ? 'Loading overview' : `Updated ${formatDateTime(overview.freshness.calculatedAt)}`}
        </div>
      </div>

      <div className="metric-grid overview-metrics">
        <MetricCard label={overview.scope.label} value={currency(overview.accountBalanceMinorUnits, overview.currency)} />
        <MetricCard label="This month spent" value={currency(overview.currentMonthSpendMinorUnits, overview.currency)} />
        <MetricCard label="Avg daily spend" value={currency(overview.averageDailySpendMinorUnits, overview.currency)} />
      </div>

      <section className="panel">
        <div className="panel-header">
          <div>
            <p>Monthly cash flow race</p>
            <h2>Income vs expenses</h2>
          </div>
          <WalletCards aria-hidden="true" />
        </div>
        <CashFlowRace overview={overview} />
      </section>

      <div className="overview-grid">
        <section className="panel">
          <div className="panel-header">
            <div>
              <p>{formatMonth(overview.monthKey)}</p>
              <h2>Daily cash flow</h2>
            </div>
          </div>
          <DailyCashFlowChart days={overview.dailyCashFlow} currencyCode={overview.currency} />
        </section>

        <section className="panel">
          <div className="panel-header">
            <div>
              <p>{formatMonth(overview.monthKey)}</p>
              <h2>Spend by tag</h2>
            </div>
          </div>
          <SpendByTagChart tags={overview.monthlySpendByTag} currencyCode={overview.currency} />
        </section>
      </div>
    </section>
  )
}

function MetricCard({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

function CashFlowRace({ overview }: { overview: OverviewResponse }) {
  const income = overview.cashFlowRace.incomeMinorUnits
  const expenses = overview.cashFlowRace.expenseMinorUnits
  const total = Math.max(income + expenses, 1)
  const incomePercent = (income / total) * 100
  const expensePercent = (expenses / total) * 100

  return (
    <div className="cash-flow-race">
      <div className="cash-flow-values">
        <div>
          <span>Income</span>
          <strong>{currency(income, overview.currency)}</strong>
        </div>
        <div>
          <span>Net</span>
          <strong>{signedCurrency(overview.cashFlowRace.netMinorUnits, overview.currency)}</strong>
        </div>
        <div>
          <span>Expenses</span>
          <strong>{currency(expenses, overview.currency)}</strong>
        </div>
      </div>
      <div className="race-track" aria-label="Income vs expenses">
        <div className="race-income" style={{ width: `${incomePercent}%` }} />
        <div className="race-expense" style={{ width: `${expensePercent}%` }} />
      </div>
    </div>
  )
}

function DailyCashFlowChart({ days, currencyCode }: { days: OverviewResponse['dailyCashFlow']; currencyCode: string }) {
  const max = Math.max(...days.map(x => x.incomeMinorUnits + x.expenseMinorUnits), 1)

  return (
    <div className="daily-chart">
      <div className="daily-bars" style={{ gridTemplateColumns: `repeat(${Math.max(days.length, 1)}, minmax(0, 1fr))` }}>
        {days.map(x => {
          const incomeHeight = Math.max((x.incomeMinorUnits / max) * 100, x.incomeMinorUnits > 0 ? 3 : 0)
          const expenseHeight = Math.max((x.expenseMinorUnits / max) * 100, x.expenseMinorUnits > 0 ? 3 : 0)
          return (
            <div className="daily-bar" key={x.date}>
              <div className="daily-tooltip">
                <strong>{formatChartDate(x.date)}</strong>
                <span>Income {currency(x.incomeMinorUnits, currencyCode)}</span>
                <span>Expense {currency(x.expenseMinorUnits, currencyCode)}</span>
              </div>
              <div className="daily-bar-stack">
                {x.incomeMinorUnits > 0 && <div className="daily-income" style={{ height: `${incomeHeight}%` }} />}
                {x.expenseMinorUnits > 0 && <div className="daily-expense" style={{ height: `${expenseHeight}%` }} />}
              </div>
              <span>{x.day === 1 || x.day % 7 === 0 ? x.day : ''}</span>
            </div>
          )
        })}
      </div>
      <div className="chart-legend">
        <span><i className="legend-income" />Income</span>
        <span><i className="legend-expense" />Expense</span>
      </div>
    </div>
  )
}

function SpendByTagChart({ tags, currencyCode }: { tags: OverviewResponse['monthlySpendByTag']; currencyCode: string }) {
  const total = tags.reduce((x, y) => x + y.amountMinorUnits, 0)
  const primary = tags[0]
  const background = total > 0
    ? `conic-gradient(${getPieStops(tags).join(', ')})`
    : 'conic-gradient(#36393d 0 100%)'

  return (
    <div className="tag-chart">
      <div className="pie-chart" style={{ background }}>
        <div>
          <span>Total</span>
          <strong>{currency(total, currencyCode)}</strong>
        </div>
      </div>
      <div className="tag-list">
        {tags.map(x => (
          <div key={x.tagId ?? 'untagged'}>
            <span><i style={{ backgroundColor: x.color }} />{x.name}</span>
            <strong>{currency(x.amountMinorUnits, currencyCode)}</strong>
          </div>
        ))}
        {primary && <p>{primary.name} is currently {primary.percentage.toFixed(1)}% of tracked monthly spend.</p>}
      </div>
    </div>
  )
}

function ConnectionsPage() {
  return (
    <section className="page">
      <header className="page-header">
        <div>
          <p>Provider access</p>
          <h1>Connections</h1>
        </div>
      </header>
      <button type="button">Connect bank</button>
    </section>
  )
}

function SettingsPage() {
  return (
    <section className="page">
      <header className="page-header">
        <div>
          <p>Account</p>
          <h1>Settings</h1>
        </div>
      </header>
      <button type="button">Manage security</button>
    </section>
  )
}

async function getOverview(accountId: string | null) {
  const params = new URLSearchParams()
  if (accountId) {
    params.set('accountId', accountId)
  }

  return httpClient<OverviewResponse>({
    method: 'GET',
    url: `/api/overview${params.size > 0 ? `?${params}` : ''}`,
  })
}

async function refreshOverview(accountId: string | null) {
  const params = new URLSearchParams()
  if (accountId) {
    params.set('accountId', accountId)
  }

  return httpClient<OverviewResponse>({
    method: 'POST',
    url: `/api/overview/refresh${params.size > 0 ? `?${params}` : ''}`,
  })
}

function getOverviewQueryKey(accountId: string | null) {
  return ['overview', accountId ?? allAccountsValue] as const
}

function createEmptyOverview(accountId: string | null, selectedAccountId: string, accounts?: AccountResponse[]): OverviewResponse {
  const monthKey = new Date().toISOString().slice(0, 7)
  const label = accountId
    ? accounts?.find(x => x.id === selectedAccountId)?.name ?? 'Selected account'
    : 'All accounts'

  return {
    scope: { accountId, label },
    monthKey,
    currency: 'AUD',
    accountBalanceMinorUnits: 0,
    currentMonthSpendMinorUnits: 0,
    averageDailySpendMinorUnits: 0,
    cashFlowRace: { incomeMinorUnits: 0, expenseMinorUnits: 0, netMinorUnits: 0 },
    dailyCashFlow: [],
    monthlySpendByTag: [{ tagId: null, name: 'Untagged', color: '#94a3b8', amountMinorUnits: 0, percentage: 0 }],
    freshness: { calculatedAt: new Date().toISOString(), sourceWatermark: null, isRefreshing: false },
  }
}

function getPieStops(tags: OverviewResponse['monthlySpendByTag']) {
  let start = 0
  return tags.map(x => {
    const end = start + x.percentage
    const stop = `${x.color} ${start}% ${end}%`
    start = end
    return stop
  })
}

function currency(value: number, currencyCode: string) {
  return new Intl.NumberFormat(undefined, {
    currency: currencyCode,
    maximumFractionDigits: 0,
    style: 'currency',
  }).format(value / 100)
}

function signedCurrency(value: number, currencyCode: string) {
  const formatted = currency(Math.abs(value), currencyCode)
  return value > 0 ? `+${formatted}` : value < 0 ? `-${formatted}` : formatted
}

function formatDateTime(value: string) {
  return new Date(value).toLocaleString(undefined, { day: 'numeric', hour: 'numeric', minute: '2-digit', month: 'short' })
}

function formatChartDate(value: string) {
  return new Date(`${value}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })
}

function formatMonth(value: string) {
  return new Date(`${value}-01T00:00:00`).toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
}

const rootRoute = createRootRoute({ component: DashboardShell })
const indexRoute = createRoute({ getParentRoute: () => rootRoute, path: '/', component: DashboardPage })
const connectionsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/connections', component: ConnectionsPage })
const settingsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/settings', component: SettingsPage })
const routeTree = rootRoute.addChildren([indexRoute, connectionsRoute, settingsRoute])
const router = createRouter({ routeTree })

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthTokenProvider>
        <RouterProvider router={router} />
      </AuthTokenProvider>
    </QueryClientProvider>
  )
}
