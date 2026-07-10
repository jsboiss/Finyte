import { memo } from 'react'
import { AppSelect } from '../../shared/AppSelect'
import type { DashboardTimeframeOption } from '../types'

export type TimeframeSelectorVariant = 'compact' | 'normal'

export const TimeframeSelector = memo(function TimeframeSelector({
  onChange,
  timeframes,
  value,
  variant = 'normal',
}: {
  onChange: (value: string) => void
  timeframes: DashboardTimeframeOption[]
  value: string
  variant?: TimeframeSelectorVariant
}) {
  if (variant === 'compact') {
    const timeframe = timeframes.find(x => x.id === value)
    return (
      <span className="timeframe-selector-compact">
        <AppSelect
          aria-label="Reporting timeframe"
          onChange={x => onChange(x.target.value)}
          title={timeframe?.description}
          value={value}
        >
          {timeframes.map(x => <option key={x.id} value={x.id}>{x.label}</option>)}
        </AppSelect>
      </span>
    )
  }

  return (
    <div className="timeframe-selector" role="tablist" aria-label="Reporting timeframe">
      {timeframes.map(x => (
        <button
          aria-selected={x.id === value}
          className={x.id === value ? 'is-selected' : undefined}
          key={x.id}
          onClick={() => onChange(x.id)}
          role="tab"
          title={x.description}
          type="button"
        >
          {x.label}
        </button>
      ))}
    </div>
  )
})
