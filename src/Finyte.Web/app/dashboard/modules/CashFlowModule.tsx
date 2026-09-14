import { CashFlowChart } from '../charts/CashFlowChart'
import { CashFlowDashboardModule } from '../components/CashFlowDashboardModule'
import type { OverviewResponse } from '../types'

export function CashFlowModule({ overview }: { overview: OverviewResponse }) {
  return (
    <CashFlowDashboardModule
      overview={overview}
      title="Cash flow"
    >
      {(data, timeframe) => <CashFlowChart overview={overview} chart={timeframe.chart} currencyCode={overview.currency} points={data.points} />}
    </CashFlowDashboardModule>
  )
}
