import type { ReactNode } from 'react'
import { createEmptyCashFlow, getCashFlow, getCashFlowQueryKey } from '../cashFlowApi'
import { cashFlowTimeframes } from '../timeframes'
import type { CashFlowModuleData, CashFlowRangeResponse, DashboardTimeframe, OverviewResponse } from '../types'
import { TimeframedDashboardModule, type TimeframedDashboardModuleState } from './TimeframedDashboardModule'

export function CashFlowDashboardModule({
  children,
  icon,
  overview,
  title,
}: {
  children: (data: CashFlowModuleData, timeframe: DashboardTimeframe<CashFlowModuleData, CashFlowRangeResponse>, state: TimeframedDashboardModuleState) => ReactNode
  icon?: ReactNode
  overview: OverviewResponse
  title: string
}) {
  return (
    <TimeframedDashboardModule
      createEmptySource={x => createEmptyCashFlow(x, overview.currency)}
      getQueryKey={x => getCashFlowQueryKey(overview.scope.accountId, x, overview.includeInternalTransfers)}
      icon={icon}
      load={x => getCashFlow(overview.scope.accountId, x, overview.includeInternalTransfers)}
      pickerVariant="compact"
      timeframes={cashFlowTimeframes}
      title={title}
    >
      {children}
    </TimeframedDashboardModule>
  )
}
