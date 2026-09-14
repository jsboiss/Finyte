import { formatChartDate } from '../shared/formatters'
import type {
  CashFlowModuleData,
  CashFlowRangeResponse,
  DashboardDateRange,
  DashboardTimeframe,
} from './types'

export const cashFlowTimeframes: DashboardTimeframe<CashFlowModuleData, CashFlowRangeResponse>[] = [
  {
    id: 'trailing-7-days',
    label: '7 days',
    description: 'The last 7 days including today',
    kind: 'rolling',
    chart: 'daily-bars',
    getRange: x => getTrailingRange(x, 7),
    getData: createDailyCashFlowData,
  },
  {
    id: 'trailing-30-days',
    label: '30 days',
    description: 'The last 30 days including today',
    kind: 'rolling',
    chart: 'weekly-bars',
    getRange: x => getTrailingRange(x, 30),
    getData: createWeeklyCashFlowData,
  },
  {
    id: 'month-to-date',
    label: 'This month',
    description: 'From the start of this month through today',
    kind: 'toDate',
    chart: 'weekly-bars',
    getRange: getMonthToDateRange,
    getData: createWeeklyCashFlowData,
  },
]

function createDailyCashFlowData(response: CashFlowRangeResponse): CashFlowModuleData {
  const days = response.dailyCashFlow
  return {
    points: days.map(x => ({
      id: x.date,
      from: x.date, to: x.date,
      label: x.day === 1 || x.day % 7 === 0 || days.length <= 10 ? x.day.toString() : '',
      tooltip: formatChartDate(x.date),
      incomeMinorUnits: x.incomeMinorUnits,
      expenseMinorUnits: x.expenseMinorUnits,
    })),
    periodLabel: formatRange(response.from, response.to),
  }
}

function createWeeklyCashFlowData(response: CashFlowRangeResponse): CashFlowModuleData {
  const points: CashFlowModuleData['points'] = []
  for (let index = 0; index < response.dailyCashFlow.length; index += 7) {
    const days = response.dailyCashFlow.slice(index, index + 7)
    points.push({
      id: `${response.from}-week-${points.length + 1}`,
      from: days[0].date, to: days[days.length - 1].date,
      label: `W${points.length + 1}`,
      tooltip: formatRange(days[0].date, days[days.length - 1].date),
      incomeMinorUnits: days.reduce((x, y) => x + y.incomeMinorUnits, 0),
      expenseMinorUnits: days.reduce((x, y) => x + y.expenseMinorUnits, 0),
    })
  }

  return {
    points,
    periodLabel: formatRange(response.from, response.to),
  }
}

function getTrailingRange(today: Date, dayCount: number): DashboardDateRange {
  const from = new Date(today)
  from.setDate(from.getDate() - dayCount + 1)
  return { from: formatDate(from), to: formatDate(today) }
}

function getMonthToDateRange(today: Date): DashboardDateRange {
  const from = new Date(today.getFullYear(), today.getMonth(), 1)
  return { from: formatDate(from), to: formatDate(today) }
}

function formatDate(value: Date) {
  const year = value.getFullYear()
  const month = (value.getMonth() + 1).toString().padStart(2, '0')
  const day = value.getDate().toString().padStart(2, '0')
  return `${year}-${month}-${day}`
}

function formatRange(from: string, to: string) {
  return `${formatChartDate(from)} - ${formatChartDate(to)}`
}
