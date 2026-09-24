export type OverviewResponse = {
  includeInternalTransfers?: boolean
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
    isStale: boolean
    hasFailed: boolean
    lastError: string | null
  }
  balanceCoverage: { coveredAccounts: number; totalAccounts: number; missingAccounts: string[] } | null
}

export type OverviewAccountOption = {
  id: string
  label: string
}

export type DashboardMetric = {
  href?: string
  id: string
  label: string
  value: string
  note?: string
  tone?: 'default' | 'positive' | 'negative'
}

export type DashboardTimeframeKind = 'rolling' | 'calendar' | 'toDate' | 'custom'

export type DashboardChart = 'daily-bars' | 'summary' | 'weekly-bars'

export type DashboardDateRange = {
  from: string
  to: string
}

export type DashboardTimeframeOption = {
  id: string
  label: string
  description: string
  kind: DashboardTimeframeKind
  chart: DashboardChart
}

export type DashboardTimeframe<TData, TSource> = DashboardTimeframeOption & {
  getRange: (today: Date) => DashboardDateRange
  getData: (source: TSource) => TData
}

export type CashFlowPoint = {
  from: string
  to: string
  id: string
  label: string
  tooltip: string
  incomeMinorUnits: number
  expenseMinorUnits: number
}

export type CashFlowModuleData = {
  points: CashFlowPoint[]
  periodLabel: string
}

export type CashFlowRangeResponse = {
  from: string
  to: string
  currency: string
  dailyCashFlow: OverviewResponse['dailyCashFlow']
}
