import { Help } from '../shared/Help'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { RefreshCcw } from '../shared/Icons'
import { useMemo, useState } from 'react'
import { getAccounts, type Account as AccountResponse } from '../accounts/accountsApi'
import { BillingAccessPanel } from '../billing/BillingAccessPanel'
import { getBillingAccess } from '../billing/billingApi'
import { AppSelect } from '../shared/AppSelect'
import { currency, formatMonth } from '../shared/formatters'
import { DashboardMetricGrid } from './components/DashboardMetricGrid'
import { DashboardModuleFrame } from './components/DashboardModuleFrame'
import { getOverview, getOverviewQueryKey, refreshOverview } from './overviewApi'
import { CashFlowModule } from './modules/CashFlowModule'
import { CashFlowRaceModule } from './modules/CashFlowRaceModule'
import { SpendByTagChart } from './modules/SpendByTagChart'
import { transactionLink } from './transactionLinks'
import type { DashboardMetric, OverviewAccountOption, OverviewResponse } from './types'

const allAccountsValue = 'all'

export function DashboardPage() {
  const [selectedAccountId, setSelectedAccountId] = useState(allAccountsValue)
  const [includeInternalTransfers, setIncludeInternalTransfers] = useState(false)
  const queryClient = useQueryClient()
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
    queryKey: getOverviewQueryKey(accountId, includeInternalTransfers),
    queryFn: () => getOverview(accountId, includeInternalTransfers),
    enabled: hasBillingAccess,
    refetchInterval: x => x.state.data?.freshness.isRefreshing ? 1_000 : false,
    staleTime: 60_000,
  })
  const refreshOverviewMutation = useMutation({
    mutationFn: (scope: { accountId: string | null; includeInternalTransfers: boolean }) => refreshOverview(scope.accountId, scope.includeInternalTransfers),
    onSuccess: (x, scope) => {
      queryClient.setQueryData(getOverviewQueryKey(scope.accountId, scope.includeInternalTransfers), x)
    },
  })
  const accountOptions = useMemo<OverviewAccountOption[]>(() => [
    { id: allAccountsValue, label: 'All accounts' },
    ...(accountsQuery.data ?? []).map(x => ({ id: x.id, label: x.name })),
  ], [accountsQuery.data])

  const overview = overviewQuery.data ?? { ...createEmptyOverview(accountId, selectedAccountId, accountsQuery.data), includeInternalTransfers }
  const metrics: DashboardMetric[] = [
    { id: 'balance', label: overview.scope.label, value: currency(overview.accountBalanceMinorUnits, overview.currency), href: '/accounts' },
    { id: 'month-spend', label: 'This month spent', value: currency(overview.currentMonthSpendMinorUnits, overview.currency), href: transactionLink(overview, 'debit') },
    { id: 'daily-spend', label: 'Avg daily spend', value: currency(overview.averageDailySpendMinorUnits, overview.currency), href: transactionLink(overview, 'debit') },
  ]

  if (billingAccessQuery.isLoading || (hasBillingAccess && overviewQuery.isPending)) {
    return <section className="page"><p role="status">Loading dashboard…</p></section>
  }
  if (billingAccessQuery.isError || (hasBillingAccess && overviewQuery.isError)) {
    return <section className="page"><p role="alert">Unable to load dashboard.</p><button type="button" onClick={() => { void billingAccessQuery.refetch(); void overviewQuery.refetch() }}>Retry</button></section>
  }

  if (!billingAccessQuery.isLoading && !hasBillingAccess) {
    return <LockedDashboard />
  }

  return (
    <section className="page">
      <div className="overview-controls"><div className="page-title"><h1>Dashboard</h1><Help title="About these totals"><p>Spending and income {includeInternalTransfers ? 'include' : 'exclude'} matched internal transfers. Balances include all account movements.</p>
        {accountId === null && (accountsQuery.data?.filter(x => !x.includeInAnalytics).length ?? 0) > 0 && <p>{accountsQuery.data?.filter(x => !x.includeInAnalytics).length} accounts excluded from combined spending and income.</p>}
        <label className="transfer-comparison-toggle"><input type="checkbox" checked={includeInternalTransfers} onChange={x => setIncludeInternalTransfers(x.target.checked)} /><span>Include confirmed internal transfers</span></label>
        <p><Link to="/accounts">Account preferences</Link></p>
      </Help></div>
        <div className="overview-actions">
          <label>
            <AppSelect aria-label="Account" value={selectedAccountId} onChange={x => setSelectedAccountId(x.target.value)}>
              {accountOptions.map(x => <option key={x.id} value={x.id}>{x.label}</option>)}
            </AppSelect>
          </label>

          <button
            aria-label="Refresh overview"
            className="icon-button desktop-refresh-button"
            disabled={overviewQuery.isFetching || refreshOverviewMutation.isPending}
            onClick={() => refreshOverviewMutation.mutate({ accountId, includeInternalTransfers })}
            title="Refresh overview"
            type="button"
          >
            <RefreshCcw aria-hidden="true" />
          </button>
        </div>
      </div>

      <DashboardMetricGrid metrics={metrics} />


      <CashFlowRaceModule overview={overview} />

      <div className="overview-grid">
        <CashFlowModule overview={overview} />

        <DashboardModuleFrame eyebrow={formatMonth(overview.monthKey)} title="Spend by tag">
          <SpendByTagChart tags={overview.monthlySpendByTag} currencyCode={overview.currency} overview={overview} />
        </DashboardModuleFrame>
      </div>
    </section>
  )
}

function LockedDashboard() {
  return (
    <section className="page">
      <BillingAccessPanel compact />
    </section>
  )
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
