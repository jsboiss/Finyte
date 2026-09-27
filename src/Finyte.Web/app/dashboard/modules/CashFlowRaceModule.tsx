import { scopeOf } from '../accountScope'
import { WalletCards } from '../../shared/Icons'
import { CashFlowDashboardModule } from '../components/CashFlowDashboardModule'
import type { OverviewResponse } from '../types'
import { CashFlowRace } from './CashFlowRace'

export function CashFlowRaceModule({ overview }: { overview: OverviewResponse }) {
  return (
    <CashFlowDashboardModule
      icon={<WalletCards aria-hidden="true" />}
      overview={overview}
      title="Money in vs money out"
    >
      {(data, _timeframe, state) => (
        <CashFlowRace
          overview={overview}
          currencyCode={overview.currency}
          data={data}
          isLoading={state.isLoading}
          key={`${scopeOf(overview)}-${overview.includeInternalTransfers}`}
        />
      )}
    </CashFlowDashboardModule>
  )
}
