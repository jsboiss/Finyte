import { memo } from 'react'
import type { DashboardMetric } from '../types'

export const DashboardMetricGrid = memo(function DashboardMetricGrid({ metrics }: { metrics: DashboardMetric[] }) {
  return (
    <div className="metric-grid overview-metrics">
      {metrics.map(x => (
        <a href={x.href} className={x.tone ? `metric-card metric-card-${x.tone}` : 'metric-card'} key={x.id}>
          <span>{x.label}</span>
          <strong>{x.value}</strong>
        </a>
      ))}
    </div>
  )
})
