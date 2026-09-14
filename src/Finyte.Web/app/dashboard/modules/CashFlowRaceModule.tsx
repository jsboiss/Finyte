import { WalletCards } from '../../shared/Icons'
import { CashFlowDashboardModule } from '../components/CashFlowDashboardModule'
import type { OverviewResponse } from '../types'
import { CashFlowRace } from './CashFlowRace'

export function CashFlowRaceModule({ overview }: { overview: OverviewResponse }) {
  return (
    <CashFlowDashboardModule
      icon={<WalletCards aria-hidden="true" />}
      overview={overview}
      title="Income vs expenses"
    >
      {(data, _timeframe, state) => (
        <CashFlowRace
          currencyCode={overview.currency}
          data={data}
          isLoading={state.isLoading}
          key={overview.scope.accountId ?? 'all'}
        />
      )}
    </CashFlowDashboardModule>
  )
}
