import { SignIn, UserButton, useAuth } from '@clerk/react'
import { keepPreviousData, QueryClient, QueryClientProvider, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createRootRoute, createRoute, createRouter, Link, Outlet, RouterProvider } from '@tanstack/react-router'
import { createColumnHelper, flexRender, getCoreRowModel, getFilteredRowModel, type ColumnFiltersState, useReactTable } from '@tanstack/react-table'
import { Activity, Banknote, CalendarClock, CreditCard, Home, Loader2, Plus, ReceiptText, RefreshCcw, Settings, SlidersHorizontal, Tags, Trash2, WalletCards, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
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

type CheckoutSessionResponse = {
  url: string
}

type PortalSessionResponse = {
  url: string
}

type BillingAccessResponse = {
  hasAccess: boolean
  status: string | null
  stripePriceId: string | null
  currentPeriodEnd: string | null
  cancelAtPeriodEnd: boolean
}

type BillingPlan = {
  key: string
  name: string
  cadence: string
  detail: string
}

type Transaction = {
  id: string
  accountId: string
  accountDisplayName: string
  postedDate: string
  description: string
  merchantName: string | null
  category: string
  amountMinorUnits: number
  currency: string
  tags: TransactionTag[]
}

type TransactionPage = {
  items: Transaction[]
  page: number
  pageSize: number
  totalCount: number
}

type TransactionTag = {
  id: string
  name: string
  color: string
}

type MerchantTagRule = {
  id: string
  merchantName: string
  tag: TransactionTag
}

type DateFilter = {
  from?: string
  to?: string
}

type AmountFilter = {
  min?: string
  max?: string
}

type CreateTagInput = {
  name: string
  color: string
}

type SetTransactionTagsInput = {
  transactionId: string
  tagIds: string[]
}

type CreateMerchantRuleInput = {
  merchantName: string
  tagId: string
}

const allAccountsValue = 'all'
const queryClient = new QueryClient()
const devAuthEnabled = import.meta.env.VITE_DEV_AUTH === 'true'
const tagColorOptions = ['#bae6fd', '#bbf7d0', '#fde68a', '#fecdd3', '#ddd6fe', '#fed7aa', '#ccfbf1', '#e9d5ff']
const transactionsPageSize = 25
const transactionColumnHelper = createColumnHelper<Transaction>()
const billingPlans: BillingPlan[] = [
  { key: 'Monthly', name: 'Monthly', cadence: 'Month to month', detail: 'Flexible access for early households.' },
  { key: 'Yearly', name: 'Yearly', cadence: 'Annual', detail: 'One yearly subscription for ongoing access.' },
]

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
          <Link to="/transactions" activeProps={{ className: 'active' }}>
            <ReceiptText aria-hidden="true" />
            Transactions
          </Link>
          <Link to="/billing" activeProps={{ className: 'active' }}>
            <CreditCard aria-hidden="true" />
            Billing
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
  const billingAccessQuery = useQuery({
    queryKey: ['billing-access'],
    queryFn: () => getBillingAccess(),
    staleTime: 30_000,
  })
  const hasBillingAccess = billingAccessQuery.data?.hasAccess === true
  const accountsQuery = useQuery({
    queryKey: ['accounts'],
    queryFn: () => getAccounts(),
    enabled: hasBillingAccess,
    staleTime: 60_000,
  })
  const accountId = selectedAccountId === allAccountsValue ? null : selectedAccountId
  const overviewQuery = useQuery({
    queryKey: getOverviewQueryKey(accountId),
    queryFn: () => getOverview(accountId),
    enabled: hasBillingAccess,
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
    if (!hasBillingAccess || !accountsQuery.data) {
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
  }, [accountsQuery.data, hasBillingAccess, queryClient])

  const overview = overviewQuery.data ?? createEmptyOverview(accountId, selectedAccountId, accountsQuery.data)
  const isLoading = billingAccessQuery.isLoading || overviewQuery.isLoading || accountsQuery.isLoading

  if (!billingAccessQuery.isLoading && !hasBillingAccess) {
    return <LockedDashboard statusQuery={statusQuery.data?.databaseAvailable} />
  }

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

function LockedDashboard({ statusQuery }: { statusQuery?: boolean }) {
  return (
    <section className="page">
      <header className="page-header">
        <div>
          <p>Household dashboard</p>
          <h1>Financial overview</h1>
        </div>
        <div className="status-pill">
          <CalendarClock aria-hidden="true" />
          {statusQuery ? 'API connected' : 'Waiting for API'}
        </div>
      </header>

      <BillingAccessPanel compact />
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

function TransactionsPage() {
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([])
  const [showFilters, setShowFilters] = useState(false)
  const [showTagManagement, setShowTagManagement] = useState(false)
  const [tagName, setTagName] = useState('')
  const [tagColor, setTagColor] = useState('#64748b')
  const [merchantName, setMerchantName] = useState('')
  const [merchantTagId, setMerchantTagId] = useState('')
  const [transactionPage, setTransactionPage] = useState(1)
  const queryClient = useQueryClient()
  const transactionsQuery = useQuery({
    queryKey: ['transactions', transactionPage],
    queryFn: () => getTransactions(transactionPage, transactionsPageSize),
    placeholderData: keepPreviousData,
  })
  const accountsQuery = useQuery({
    queryKey: ['accounts'],
    queryFn: () => getAccounts(),
    staleTime: 60_000,
  })
  const tagsQuery = useQuery({
    queryKey: ['tags'],
    queryFn: () => getTags(),
  })
  const merchantRulesQuery = useQuery({
    enabled: showTagManagement,
    queryKey: ['merchant-tags'],
    queryFn: () => getMerchantRules(),
  })
  const createTagMutation = useMutation({
    mutationFn: (input: CreateTagInput) => createTag(input),
    onMutate: async input => {
      await queryClient.cancelQueries({ queryKey: ['tags'] })
      const previousTags = queryClient.getQueryData<TransactionTag[]>(['tags'])
      const optimisticTag = { id: `pending-${crypto.randomUUID()}`, name: input.name, color: input.color }
      queryClient.setQueryData<TransactionTag[]>(['tags'], x => [...(x ?? []), optimisticTag])
      setTagName('')
      return { optimisticTagId: optimisticTag.id, previousTags }
    },
    onError: (_error, _input, context) => {
      queryClient.setQueryData(['tags'], context?.previousTags)
    },
    onSuccess: (tag, _input, context) => {
      queryClient.setQueryData<TransactionTag[]>(['tags'], x => uniqueTagsById((x ?? []).map(y => y.id === context.optimisticTagId ? tag : y)))
    },
  })
  const deleteTagMutation = useMutation({
    mutationFn: (tagId: string) => deleteTag(tagId),
    onMutate: async tagId => {
      await queryClient.cancelQueries({ queryKey: ['tags'] })
      await queryClient.cancelQueries({ queryKey: ['transactions'] })
      await queryClient.cancelQueries({ queryKey: ['merchant-tags'] })
      const previousTags = queryClient.getQueryData<TransactionTag[]>(['tags'])
      const previousTransactions = queryClient.getQueryData<TransactionPage>(['transactions', transactionPage])
      const previousMerchantRules = queryClient.getQueryData<MerchantTagRule[]>(['merchant-tags'])
      queryClient.setQueryData<TransactionTag[]>(['tags'], x => (x ?? []).filter(y => y.id !== tagId))
      queryClient.setQueryData<TransactionPage>(['transactions', transactionPage], x => x ? { ...x, items: x.items.map(y => ({ ...y, tags: y.tags.filter(z => z.id !== tagId) })) } : x)
      queryClient.setQueryData<MerchantTagRule[]>(['merchant-tags'], x => (x ?? []).filter(y => y.tag.id !== tagId))
      return { previousTags, previousTransactions, previousMerchantRules }
    },
    onError: (_error, _tagId, context) => {
      queryClient.setQueryData(['tags'], context?.previousTags)
      queryClient.setQueryData(['transactions', transactionPage], context?.previousTransactions)
      queryClient.setQueryData(['merchant-tags'], context?.previousMerchantRules)
    },
  })
  const updateTransactionTagsMutation = useMutation({
    mutationFn: (input: SetTransactionTagsInput) => setTransactionTags(input),
    onMutate: async input => {
      await queryClient.cancelQueries({ queryKey: ['transactions'] })
      const previousTransactions = queryClient.getQueryData<TransactionPage>(['transactions', transactionPage])
      const allTags = queryClient.getQueryData<TransactionTag[]>(['tags']) ?? []
      const nextTags = allTags.filter(x => input.tagIds.includes(x.id))
      queryClient.setQueryData<TransactionPage>(['transactions', transactionPage], x => x ? { ...x, items: x.items.map(y => y.id === input.transactionId ? { ...y, tags: nextTags } : y) } : x)
      return { previousTransactions }
    },
    onError: (_error, _input, context) => {
      queryClient.setQueryData(['transactions', transactionPage], context?.previousTransactions)
    },
    onSuccess: (nextTags, input) => {
      queryClient.setQueryData<TransactionPage>(['transactions', transactionPage], x => x ? { ...x, items: x.items.map(y => y.id === input.transactionId ? { ...y, tags: nextTags } : y) } : x)
      queryClient.invalidateQueries({ queryKey: ['overview'] })
    },
  })
  const setTransactionTagIds = useCallback((transactionId: string, tagIds: string[]) => {
    updateTransactionTagsMutation.mutate({ transactionId, tagIds })
  }, [updateTransactionTagsMutation])
  const createMerchantRuleMutation = useMutation({
    mutationFn: (input: CreateMerchantRuleInput) => createMerchantRule(input),
    onMutate: async input => {
      await queryClient.cancelQueries({ queryKey: ['merchant-tags'] })
      await queryClient.cancelQueries({ queryKey: ['transactions'] })
      const previousMerchantRules = queryClient.getQueryData<MerchantTagRule[]>(['merchant-tags'])
      const previousTransactions = queryClient.getQueryData<TransactionPage>(['transactions', transactionPage])
      const tag = queryClient.getQueryData<TransactionTag[]>(['tags'])?.find(x => x.id === input.tagId)
      const optimisticRuleId = `pending-${crypto.randomUUID()}`
      if (tag) {
        const ruleMerchantKey = getMerchantKey(input.merchantName)
        queryClient.setQueryData<MerchantTagRule[]>(['merchant-tags'], x => [...(x ?? []), { id: optimisticRuleId, merchantName: input.merchantName, tag }])
        queryClient.setQueryData<TransactionPage>(['transactions', transactionPage], x => x ? {
          ...x,
          items: x.items.map(y => {
          const transactionMerchantName = y.merchantName?.trim() ? y.merchantName : y.description
          if (!transactionMerchantName || !matchesMerchantRule(getMerchantKey(transactionMerchantName), ruleMerchantKey) || y.tags.some(z => z.id === tag.id)) {
            return y
          }

          return { ...y, tags: [...y.tags, tag] }
          }),
        } : x)
      }

      setMerchantName('')
      return { optimisticRuleId, previousMerchantRules, previousTransactions }
    },
    onError: (_error, _input, context) => {
      queryClient.setQueryData(['merchant-tags'], context?.previousMerchantRules)
      queryClient.setQueryData(['transactions', transactionPage], context?.previousTransactions)
    },
    onSuccess: async (rule, _input, context) => {
      queryClient.setQueryData<MerchantTagRule[]>(['merchant-tags'], x => uniqueMerchantRulesById((x ?? []).map(y => y.id === context.optimisticRuleId ? rule : y)))
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['transactions'] }),
        queryClient.invalidateQueries({ queryKey: ['overview'] }),
      ])
    },
  })
  const deleteMerchantRuleMutation = useMutation({
    mutationFn: (ruleId: string) => deleteMerchantRule(ruleId),
    onMutate: async ruleId => {
      await queryClient.cancelQueries({ queryKey: ['merchant-tags'] })
      const previousMerchantRules = queryClient.getQueryData<MerchantTagRule[]>(['merchant-tags'])
      queryClient.setQueryData<MerchantTagRule[]>(['merchant-tags'], x => (x ?? []).filter(y => y.id !== ruleId))
      return { previousMerchantRules }
    },
    onError: (_error, _ruleId, context) => {
      queryClient.setQueryData(['merchant-tags'], context?.previousMerchantRules)
    },
  })
  const columns = useMemo(() => [
    transactionColumnHelper.accessor('postedDate', {
      header: 'Date',
      filterFn: (x, y, z: DateFilter) => {
        const value = x.getValue<string>(y)
        return (!z.from || value >= z.from) && (!z.to || value <= z.to)
      },
    }),
    transactionColumnHelper.accessor('accountId', {
      header: 'Account',
      cell: x => (
        <span className="account-chip" style={{ '--account-hue': getAccountHue(x.getValue()) } as CSSProperties}>
          {x.row.original.accountDisplayName}
        </span>
      ),
      filterFn: (x, y, z: string) => x.getValue<string>(y) === z,
    }),
    transactionColumnHelper.accessor('description', {
      header: 'Description',
      cell: x => (
        <div className="transaction-description">
          <strong>{x.getValue()}</strong>
          <span>{x.row.original.merchantName ?? ''}</span>
        </div>
      ),
      filterFn: 'includesString',
    }),
    transactionColumnHelper.accessor('category', {
      header: 'Category',
      filterFn: 'includesString',
    }),
    transactionColumnHelper.accessor('tags', {
      header: 'Tags',
      cell: x => (
        <TagEditor
          allTags={tagsQuery.data ?? []}
          selectedTags={x.row.original.tags}
          onChange={y => setTransactionTagIds(x.row.original.id, y)}
        />
      ),
      filterFn: (x, y, z: string) => x.getValue<TransactionTag[]>(y).some(a => a.name.toLowerCase().includes(z.toLowerCase())),
    }),
    transactionColumnHelper.accessor('amountMinorUnits', {
      header: 'Amount',
      cell: x => <span className={x.getValue() < 0 ? 'amount-negative' : 'amount-positive'}>{currency(x.getValue(), x.row.original.currency)}</span>,
      filterFn: (x, y, z: AmountFilter) => {
        const value = x.getValue<number>(y) / 100
        const min = z.min ? Number(z.min) : null
        const max = z.max ? Number(z.max) : null
        return (min == null || value >= min) && (max == null || value <= max)
      },
    }),
  ], [setTransactionTagIds, tagsQuery.data])
  const table = useReactTable({
    data: transactionsQuery.data?.items ?? [],
    columns,
    state: { columnFilters, columnVisibility: { category: false } },
    onColumnFiltersChange: setColumnFilters,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
  })
  const hasFilters = columnFilters.length > 0
  const isLoading = transactionsQuery.isLoading || transactionsQuery.isFetching
  const totalTransactions = transactionsQuery.data?.totalCount ?? 0
  const totalPages = Math.max(Math.ceil(totalTransactions / transactionsPageSize), 1)
  const canGoToPreviousPage = transactionPage > 1
  const canGoToNextPage = transactionPage < totalPages

  return (
    <section className="page transactions-page">
      <header className="page-header transactions-header">
        <div>
          <p>Ledger</p>
          <h1>Transactions</h1>
        </div>
        <div className="transactions-actions">
          <button className={showTagManagement ? 'secondary-button is-active' : 'secondary-button'} onClick={() => setShowTagManagement(x => !x)} type="button">
            <Tags aria-hidden="true" />
            Tags
          </button>
          <button className={showFilters ? 'secondary-button is-active' : 'secondary-button'} onClick={() => setShowFilters(x => !x)} type="button">
            <SlidersHorizontal aria-hidden="true" />
            Filters
          </button>
          <button className="secondary-button" disabled={!hasFilters} onClick={() => table.resetColumnFilters()} type="button">
            <X aria-hidden="true" />
            Clear
          </button>
        </div>
      </header>

      <div className="transactions-toolbar">
        <div>
          <Loader2 className={isLoading ? 'spin-visible' : ''} aria-hidden="true" />
          <span>Showing {table.getRowModel().rows.length} of {transactionsQuery.data?.items.length ?? 0} on page {transactionPage} of {totalPages} ({totalTransactions} total)</span>
        </div>
        <div className="transaction-pagination">
          <button className="secondary-button" disabled={!canGoToPreviousPage || isLoading} onClick={() => setTransactionPage(x => Math.max(1, x - 1))} type="button">
            Previous
          </button>
          <button className="secondary-button" disabled={!canGoToNextPage || isLoading} onClick={() => setTransactionPage(x => x + 1)} type="button">
            Next
          </button>
        </div>
      </div>

      {showTagManagement && (
        <section className="panel tag-management-panel">
          <div className="tag-management-column">
            <div className="panel-header compact-panel-header">
              <div>
                <p>Labels</p>
                <h2>Tags</h2>
              </div>
            </div>
            <div className="tag-form">
              <input onChange={x => setTagName(x.target.value)} placeholder="New tag" value={tagName} />
              <div className="tag-color-picker">
                {tagColorOptions.map(x => (
                  <button
                    aria-label={`Use tag color ${x}`}
                    className={tagColor.toLowerCase() === x ? 'is-selected' : ''}
                    key={x}
                    onClick={() => setTagColor(x)}
                    style={{ backgroundColor: x }}
                    type="button"
                  />
                ))}
                <input aria-label="Custom tag color" onChange={x => setTagColor(x.target.value)} type="color" value={tagColor} />
              </div>
              <button disabled={!tagName.trim() || createTagMutation.isPending} onClick={() => createTagMutation.mutate({ name: tagName.trim(), color: tagColor })} type="button">
                <Plus aria-hidden="true" />
                Add
              </button>
            </div>
            <div className="tag-pill-list">
              {(tagsQuery.data ?? []).map(x => (
                <span className="editable-tag-pill" key={x.id}>
                  <TagPill tag={x} />
                  <button aria-label={`Delete tag ${x.name}`} onClick={() => deleteTagMutation.mutate(x.id)} type="button">
                    <Trash2 aria-hidden="true" />
                  </button>
                </span>
              ))}
              {!tagsQuery.isLoading && (tagsQuery.data?.length ?? 0) === 0 && <span className="empty-inline">No tags yet.</span>}
            </div>
          </div>
          <div className="tag-management-column merchant-rules-column">
            <div className="panel-header compact-panel-header">
              <div>
                <p>Automation</p>
                <h2>Merchant tag rules</h2>
              </div>
            </div>
            <div className="merchant-rule-form">
              <input onChange={x => setMerchantName(x.target.value)} placeholder="Merchant name" value={merchantName} />
              <select onChange={x => setMerchantTagId(x.target.value)} value={merchantTagId}>
                <option value="">Select tag</option>
                {(tagsQuery.data ?? []).map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
              </select>
              <button disabled={!merchantName.trim() || !merchantTagId || createMerchantRuleMutation.isPending} onClick={() => createMerchantRuleMutation.mutate({ merchantName: merchantName.trim(), tagId: merchantTagId })} type="button">
                <Plus aria-hidden="true" />
                Rule
              </button>
            </div>
            <div className="tag-pill-list">
              {(merchantRulesQuery.data ?? []).map(x => (
                <span className="merchant-rule-pill" key={x.id}>
                  {x.merchantName}
                  <TagPill tag={x.tag} />
                  <button aria-label={`Delete rule for ${x.merchantName}`} onClick={() => deleteMerchantRuleMutation.mutate(x.id)} type="button">
                    <Trash2 aria-hidden="true" />
                  </button>
                </span>
              ))}
              {!merchantRulesQuery.isLoading && (merchantRulesQuery.data?.length ?? 0) === 0 && <span className="empty-inline">No merchant rules yet.</span>}
            </div>
          </div>
        </section>
      )}

      {showFilters && (
        <section className="panel transaction-filters-panel">
          <FilterField className="date-filter-field" label="Date">
            <DateRangeFilter
              value={(table.getColumn('postedDate')?.getFilterValue() as DateFilter | undefined) ?? {}}
              onChange={x => table.getColumn('postedDate')?.setFilterValue(x.from || x.to ? x : undefined)}
            />
          </FilterField>
          <FilterField label="Account">
            <select
              disabled={accountsQuery.isLoading}
              onChange={x => table.getColumn('accountId')?.setFilterValue(x.target.value || undefined)}
              value={(table.getColumn('accountId')?.getFilterValue() as string | undefined) ?? ''}
            >
              <option value="">All accounts</option>
              {(accountsQuery.data ?? []).map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </FilterField>
          <FilterField label="Description">
            <DebouncedFilterInput
              onChange={x => table.getColumn('description')?.setFilterValue(x)}
              placeholder="Search descriptions"
              value={(table.getColumn('description')?.getFilterValue() as string | undefined) ?? ''}
            />
          </FilterField>
          <FilterField label="Category">
            <DebouncedFilterInput
              onChange={x => table.getColumn('category')?.setFilterValue(x)}
              placeholder="Search categories"
              value={(table.getColumn('category')?.getFilterValue() as string | undefined) ?? ''}
            />
          </FilterField>
          <FilterField label="Tags">
            <DebouncedFilterInput
              onChange={x => table.getColumn('tags')?.setFilterValue(x)}
              placeholder="Search tags"
              value={(table.getColumn('tags')?.getFilterValue() as string | undefined) ?? ''}
            />
          </FilterField>
          <FilterField className="amount-filter-field" label="Amount">
            <AmountRangeFilter
              value={(table.getColumn('amountMinorUnits')?.getFilterValue() as AmountFilter | undefined) ?? {}}
              onChange={x => table.getColumn('amountMinorUnits')?.setFilterValue(x.min || x.max ? x : undefined)}
            />
          </FilterField>
        </section>
      )}

      <section className="transaction-table-section">
        <div className={isLoading ? 'table-progress is-visible' : 'table-progress'} />
        <table>
          <thead>
            {table.getHeaderGroups().map(x => (
              <tr key={x.id}>{x.headers.map(y => <th key={y.id}>{flexRender(y.column.columnDef.header, y.getContext())}</th>)}</tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.map(x => (
              <tr key={x.id}>{x.getVisibleCells().map(y => <td key={y.id}>{flexRender(y.column.columnDef.cell, y.getContext())}</td>)}</tr>
            ))}
            {table.getRowModel().rows.length === 0 && (
              <tr>
                <td className="empty-table-cell" colSpan={5}>{hasFilters ? 'No transactions match the current filters.' : 'No transactions imported yet.'}</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </section>
  )
}

function FilterField({ label, children, className }: { label: string; children: ReactNode; className?: string }) {
  return (
    <label className={className ? `filter-field ${className}` : 'filter-field'}>
      <span>{label}</span>
      {children}
    </label>
  )
}

function DebouncedFilterInput({ value, onChange, placeholder }: { value: string; onChange: (value: string) => void; placeholder: string }) {
  const [draftValue, setDraftValue] = useState(value)
  const onChangeRef = useRef(onChange)

  useEffect(() => {
    onChangeRef.current = onChange
  }, [onChange])

  useEffect(() => {
    setDraftValue(value)
  }, [value])

  useEffect(() => {
    if (draftValue === value) {
      return
    }

    const timeout = window.setTimeout(() => onChangeRef.current(draftValue), 150)
    return () => window.clearTimeout(timeout)
  }, [draftValue, value])

  return <input onChange={x => setDraftValue(x.target.value)} placeholder={placeholder} value={draftValue} />
}

function DateRangeFilter({ value, onChange }: { value: DateFilter; onChange: (value: DateFilter) => void }) {
  return (
    <div className="range-filter">
      <input onChange={x => onChange({ ...value, from: x.target.value })} type="date" value={value.from ?? ''} />
      <input onChange={x => onChange({ ...value, to: x.target.value })} type="date" value={value.to ?? ''} />
    </div>
  )
}

function AmountRangeFilter({ value, onChange }: { value: AmountFilter; onChange: (value: AmountFilter) => void }) {
  return (
    <div className="range-filter">
      <input onChange={x => onChange({ ...value, min: x.target.value })} placeholder="Min" type="number" value={value.min ?? ''} />
      <input onChange={x => onChange({ ...value, max: x.target.value })} placeholder="Max" type="number" value={value.max ?? ''} />
    </div>
  )
}

function TagEditor({ allTags, selectedTags, onChange }: { allTags: TransactionTag[]; selectedTags: TransactionTag[]; onChange: (tagIds: string[]) => void }) {
  const [isOpen, setIsOpen] = useState(false)
  const [popupPosition, setPopupPosition] = useState<{ left: number; maxHeight: number; placement: 'above' | 'below'; top: number }>({ left: 0, maxHeight: 280, placement: 'below', top: 0 })
  const [selectedTagIds, setSelectedTagIds] = useState(() => selectedTags.map(x => x.id))
  const containerRef = useRef<HTMLDivElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const selectedIds = new Set(isOpen ? selectedTagIds : selectedTags.map(x => x.id))
  const visibleSelectedTags = allTags.filter(x => selectedIds.has(x.id))

  const updatePopupPosition = useCallback(() => {
    const rect = buttonRef.current?.getBoundingClientRect()
    if (!rect) {
      return
    }

    const viewportGap = 8
    const triggerGap = 4
    const popupMinWidth = 192
    const preferredMaxHeight = 280
    const viewportHeight = window.innerHeight
    const viewportWidth = window.innerWidth
    const availableBelow = viewportHeight - rect.bottom - viewportGap - triggerGap
    const availableAbove = rect.top - viewportGap - triggerGap
    const placement = availableBelow >= preferredMaxHeight || availableBelow >= availableAbove ? 'below' : 'above'
    const availableHeight = placement === 'below' ? availableBelow : availableAbove
    const maxHeight = Math.max(0, Math.min(preferredMaxHeight, availableHeight))
    const left = Math.min(Math.max(viewportGap, rect.left), Math.max(viewportGap, viewportWidth - popupMinWidth - viewportGap))
    const top = placement === 'below' ? rect.bottom + triggerGap : rect.top - triggerGap

    setPopupPosition({ left, maxHeight, placement, top })
  }, [])

  useEffect(() => {
    if (!isOpen) {
      return
    }

    function closeOnOutsideClick(event: MouseEvent) {
      if (!containerRef.current?.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }

    document.addEventListener('mousedown', closeOnOutsideClick)
    return () => document.removeEventListener('mousedown', closeOnOutsideClick)
  }, [isOpen])

  useEffect(() => {
    if (!isOpen) {
      return
    }

    updatePopupPosition()
    window.addEventListener('resize', updatePopupPosition)
    window.addEventListener('scroll', updatePopupPosition, true)
    return () => {
      window.removeEventListener('resize', updatePopupPosition)
      window.removeEventListener('scroll', updatePopupPosition, true)
    }
  }, [isOpen, updatePopupPosition])

  if (allTags.length === 0) {
    return <span className="empty-inline">No tags</span>
  }

  return (
    <div className="tag-editor" ref={containerRef}>
      <button
        aria-label="Edit transaction tags"
        onClick={() => {
          updatePopupPosition()
          setSelectedTagIds(selectedTags.map(x => x.id))
          setIsOpen(true)
        }}
        ref={buttonRef}
        type="button"
      >
        {visibleSelectedTags.length > 0 ? visibleSelectedTags.map(x => <TagPill key={x.id} tag={x} />) : <Plus aria-hidden="true" />}
      </button>
      {isOpen && (
        <div
          className="tag-menu"
          style={{
            left: popupPosition.left,
            maxHeight: popupPosition.maxHeight,
            top: popupPosition.top,
            transform: popupPosition.placement === 'above' ? 'translateY(-100%)' : undefined,
          }}
        >
          {allTags.map(x => (
            <label key={x.id}>
              <input
                checked={selectedIds.has(x.id)}
                onChange={y => {
                  const nextIds = y.target.checked ? [...selectedIds, x.id] : [...selectedIds].filter(z => z !== x.id)
                  setSelectedTagIds(nextIds)
                  onChange(nextIds)
                }}
                type="checkbox"
              />
              <TagPill tag={x} />
            </label>
          ))}
        </div>
      )}
    </div>
  )
}

function TagPill({ tag }: { tag: TransactionTag }) {
  return (
    <span className="tag-pill" style={{ backgroundColor: tag.color, color: getReadableTextColor(tag.color) }}>
      {tag.name}
    </span>
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

function BillingPage() {
  return (
    <section className="page billing-page">
      <header className="page-header">
        <div>
          <p>Subscription</p>
          <h1>Billing</h1>
        </div>
      </header>

      <BillingAccessPanel />
    </section>
  )
}

function BillingAccessPanel({ compact = false }: { compact?: boolean }) {
  const [selectedPlan, setSelectedPlan] = useState(billingPlans[0]?.key ?? 'Monthly')
  const [message, setMessage] = useState<string | null>(null)
  const billingAccessQuery = useQuery({
    queryKey: ['billing-access'],
    queryFn: () => getBillingAccess(),
    staleTime: 30_000,
  })
  const checkoutMutation = useMutation({
    mutationFn: (plan: string) => createCheckoutSession(plan),
    onError: () => setMessage('Checkout is not available right now.'),
    onSuccess: x => {
      window.location.assign(x.url)
    },
  })
  const portalMutation = useMutation({
    mutationFn: () => createPortalSession(),
    onError: () => setMessage('Billing portal is not available yet.'),
    onSuccess: x => {
      window.location.assign(x.url)
    },
  })
  const access = billingAccessQuery.data
  const accessLabel = access?.hasAccess
    ? access.cancelAtPeriodEnd && access.currentPeriodEnd
      ? `Access active until ${formatDate(access.currentPeriodEnd)}`
      : 'Access active'
    : 'Subscription required'

  return (
    <section className={compact ? 'panel billing-panel billing-panel-locked' : 'panel billing-panel'}>
      <div className="panel-header">
        <div>
          <p>{accessLabel}</p>
          <h2>{access?.hasAccess ? 'Manage subscription' : 'Choose access'}</h2>
        </div>
        <CreditCard aria-hidden="true" />
      </div>

      <div className="billing-plan-grid">
        {billingPlans.map(x => (
          <button
            aria-pressed={selectedPlan === x.key}
            className={selectedPlan === x.key ? 'billing-plan is-selected' : 'billing-plan'}
            key={x.key}
            onClick={() => setSelectedPlan(x.key)}
            type="button"
          >
            <span>{x.name}</span>
            <strong>{x.cadence}</strong>
            <small>{x.detail}</small>
          </button>
        ))}
      </div>

      <div className="billing-actions">
        <button
          disabled={checkoutMutation.isPending}
          onClick={() => checkoutMutation.mutate(selectedPlan)}
          type="button"
        >
          {access?.hasAccess ? 'Change plan' : 'Start checkout'}
        </button>
        <button
          className="secondary-button"
          disabled={portalMutation.isPending}
          onClick={() => portalMutation.mutate()}
          type="button"
        >
          Manage billing
        </button>
        {message && <p>{message}</p>}
      </div>
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

async function createCheckoutSession(plan: string) {
  return httpClient<CheckoutSessionResponse>({
    data: { plan },
    headers: { 'Content-Type': 'application/json' },
    method: 'POST',
    url: '/api/billing/checkout-session',
  })
}

async function createPortalSession() {
  return httpClient<PortalSessionResponse>({
    method: 'POST',
    url: '/api/billing/portal-session',
  })
}

async function getBillingAccess() {
  return httpClient<BillingAccessResponse>({
    method: 'GET',
    url: '/api/billing/access',
  })
}

async function getTransactions(page: number, pageSize: number) {
  return httpClient<TransactionPage>({
    method: 'GET',
    url: `/api/transactions?page=${page}&pageSize=${pageSize}`,
  })
}

async function getTags() {
  return httpClient<TransactionTag[]>({
    method: 'GET',
    url: '/api/tags',
  })
}

async function createTag(input: CreateTagInput) {
  return httpClient<TransactionTag>({
    data: input,
    headers: { 'Content-Type': 'application/json' },
    method: 'POST',
    url: '/api/tags',
  })
}

async function deleteTag(tagId: string) {
  return httpClient<void>({
    method: 'DELETE',
    url: `/api/tags/${tagId}`,
  })
}

async function setTransactionTags(input: SetTransactionTagsInput) {
  return httpClient<TransactionTag[]>({
    data: { tagIds: input.tagIds },
    headers: { 'Content-Type': 'application/json' },
    method: 'PUT',
    url: `/api/transactions/${input.transactionId}/tags`,
  })
}

async function getMerchantRules() {
  return httpClient<MerchantTagRule[]>({
    method: 'GET',
    url: '/api/merchant-tags',
  })
}

async function createMerchantRule(input: CreateMerchantRuleInput) {
  return httpClient<MerchantTagRule>({
    data: input,
    headers: { 'Content-Type': 'application/json' },
    method: 'POST',
    url: '/api/merchant-tags',
  })
}

async function deleteMerchantRule(ruleId: string) {
  return httpClient<void>({
    method: 'DELETE',
    url: `/api/merchant-tags/${ruleId}`,
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

function formatDate(value: string) {
  return new Date(value).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

function formatChartDate(value: string) {
  return new Date(`${value}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })
}

function formatMonth(value: string) {
  return new Date(`${value}-01T00:00:00`).toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
}

function uniqueTagsById(tags: TransactionTag[]) {
  return tags.filter((x, index) => tags.findIndex(y => y.id === x.id) === index)
}

function uniqueMerchantRulesById(rules: MerchantTagRule[]) {
  return rules.filter((x, index) => rules.findIndex(y => y.id === x.id) === index)
}

function getReadableTextColor(backgroundColor: string) {
  const hex = backgroundColor.replace('#', '')
  if (hex.length !== 6) {
    return '#ffffff'
  }

  const red = Number.parseInt(hex.slice(0, 2), 16)
  const green = Number.parseInt(hex.slice(2, 4), 16)
  const blue = Number.parseInt(hex.slice(4, 6), 16)
  const luminance = (red * 0.299 + green * 0.587 + blue * 0.114) / 255
  return luminance > 0.65 ? '#111827' : '#ffffff'
}

function getAccountHue(accountId: string) {
  const hues = [172, 205, 237, 268, 322, 24, 48, 142]
  return `${hues[getStableIndex(accountId, hues.length)]}`
}

function getStableIndex(value: string, length: number) {
  let hash = 0

  for (const character of value) {
    hash = (hash * 31 + character.charCodeAt(0)) % length
  }

  return hash
}

function getMerchantKey(merchantName: string) {
  const ignoredTokens = new Set(['au', 'aus', 'vi', 'pty', 'ltd', 'limited', 'australia', 'melbourne', 'sydney', 'brisbane', 'card', 'com'])
  return merchantName
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()
    .split(' ')
    .filter(x => x && !ignoredTokens.has(x))
    .join(' ')
}

function matchesMerchantRule(transactionMerchantKey: string, ruleMerchantKey: string) {
  return transactionMerchantKey === ruleMerchantKey || transactionMerchantKey.startsWith(`${ruleMerchantKey} `)
}

const rootRoute = createRootRoute({ component: DashboardShell })
const indexRoute = createRoute({ getParentRoute: () => rootRoute, path: '/', component: DashboardPage })
const connectionsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/connections', component: ConnectionsPage })
const transactionsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/transactions', component: TransactionsPage })
const billingRoute = createRoute({ getParentRoute: () => rootRoute, path: '/billing', component: BillingPage })
const settingsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/settings', component: SettingsPage })
const routeTree = rootRoute.addChildren([indexRoute, connectionsRoute, transactionsRoute, billingRoute, settingsRoute])
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
