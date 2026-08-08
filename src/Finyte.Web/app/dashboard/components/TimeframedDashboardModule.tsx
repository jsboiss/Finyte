import { useQuery } from '@tanstack/react-query'
import { useMemo, useState, type ReactNode } from 'react'
import type { DashboardDateRange, DashboardTimeframe } from '../types'
import { DashboardModuleFrame } from './DashboardModuleFrame'
import { TimeframeSelector, type TimeframeSelectorVariant } from './TimeframeSelector'

export type TimeframedDashboardModuleState = {
  isLoading: boolean
}

export function TimeframedDashboardModule<TData extends { periodLabel: string }, TSource>({
  children,
  createEmptySource,
  getQueryKey,
  icon,
  load,
  pickerVariant = 'normal',
  timeframes,
  title,
}: {
  children: (data: TData, timeframe: DashboardTimeframe<TData, TSource>, state: TimeframedDashboardModuleState) => ReactNode
  createEmptySource: (range: DashboardDateRange) => TSource
  getQueryKey: (range: DashboardDateRange) => readonly unknown[]
  icon?: ReactNode
  load: (range: DashboardDateRange) => Promise<TSource>
  pickerVariant?: TimeframeSelectorVariant
  timeframes: DashboardTimeframe<TData, TSource>[]
  title: string
}) {
  const [timeframeId, setTimeframeId] = useState(timeframes[0].id)
  const timeframe = timeframes.find(x => x.id === timeframeId) ?? timeframes[0]
  const range = useMemo(() => timeframe.getRange(new Date()), [timeframe])
  const sourceQuery = useQuery({
    queryKey: getQueryKey(range),
    queryFn: () => load(range),
    staleTime: 60_000,
  })
  const source = sourceQuery.data ?? createEmptySource(range)
  const data = useMemo(() => timeframe.getData(source), [source, timeframe])

  return (
    <DashboardModuleFrame
      actions={<TimeframeSelector onChange={setTimeframeId} timeframes={timeframes} value={timeframe.id} variant={pickerVariant} />}
      eyebrow={data.periodLabel}
      icon={icon}
      title={title}
    >
      {children(data, timeframe, { isLoading: sourceQuery.isPending })}
    </DashboardModuleFrame>
  )
}
