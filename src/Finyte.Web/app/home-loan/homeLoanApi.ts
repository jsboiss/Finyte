import { httpClient } from '../api/httpClient'

export type HomeLoanMonth = {
  month: string; firstApplicantIncome: number; secondApplicantIncome: number; otherIncome: number; unlabelledCredits: number
  refunds: number; gifts: number; livingExpenses: number; commitments: number; saved: number; surplus: number
}
export type LenderGroup = 'living' | 'commitment' | 'savings'
export type HomeLoanPayment = { transactionId: string; date: string; account: string; description: string; amount: number; ownCategory: string | null }
export type LenderCategory = { key: string; label: string; group: LenderGroup }
export type HomeLoanAccountRole = { accountId: string; name: string; type: string; role: 'spending' | 'savings' | 'excluded' }
export type HomeLoanPayer = {
  key: string; name: string; matchedBy: string | null; kind: string | null; applicant: number | null
  count: number; total: number; monthsSeen: number; latestDate: string; payments: HomeLoanPayment[]
}
export type HomeLoanTag = { tagId: string; name: string; color: string; category: string | null; total: number; payments: HomeLoanPayment[] }
export type HomeLoanReviewPayment = { transactionId: string; date: string; account: string; description: string; amount: number; tags: string[]; ownCategory: string | null }
export type HomeLoanReport = {
  currency: string; months: number; fromMonth: string; toMonth: string; availableMonths: string[]
  setup: { firstApplicant: string; secondApplicant: string | null; accounts: HomeLoanAccountRole[]; version: number; saved: boolean }
  monthlyTotals: HomeLoanMonth[]; average: HomeLoanMonth; total: HomeLoanMonth
  categories: { key: string; label: string; group: LenderGroup; total: number; monthlyAverage: number; byMonth: number[] }[]
  tags: HomeLoanTag[]
  payers: HomeLoanPayer[]
  untagged: { count: number; total: number }
  review: HomeLoanReviewPayment[]
  flags: { kind: string; label: string; advice: string; count: number; total: number; items: { date: string; account: string; description: string; amount: number }[] }[]
  gaps: { accountId: string; accountName: string; missingMonths: string[] }[]
  lenderCategories: LenderCategory[]
}

export const pendingCategories = ['untagged', 'unmapped', 'review']

export const getHomeLoan = (months: number, end: string) =>
  httpClient<HomeLoanReport>({ method: 'GET', url: '/api/home-loan', params: { months, end: end || undefined } })

export const saveSetup = (body: { firstApplicant: string; secondApplicant: string | null; accounts: { accountId: string; role: string }[]; expectedVersion: number }) =>
  httpClient<{ version: number }>({ method: 'PUT', url: '/api/home-loan/setup', data: body })

export const saveIncomeSource = (body: { key: string; previousKey: string | null; name: string; kind: string | null; applicant: number | null }) =>
  httpClient<void>({ method: 'PUT', url: '/api/home-loan/income-sources', data: body })

export const saveCategory = (body: { tagId: string; category: string | null }) =>
  httpClient<void>({ method: 'PUT', url: '/api/home-loan/categories', data: body })

export const savePaymentCategory = (body: { transactionId: string; category: string | null }) =>
  httpClient<void>({ method: 'PUT', url: '/api/home-loan/payment-category', data: body })

export async function downloadCsv(file: 'summary' | 'transactions', report: HomeLoanReport) {
  const blob = await httpClient<Blob>({ method: 'GET', url: `/api/home-loan/export/${file}`, params: { months: report.months, end: report.toMonth }, responseType: 'blob' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `home-loan-${file}-${report.fromMonth}-to-${report.toMonth}.csv`
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

export function matchKey(text: string) {
  return text.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim()
}

export function dayLabel(date: string) {
  const [year, month, day] = date.split('-').map(Number)
  return new Date(year, month - 1, day).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })
}

export function monthLabel(key: string) {
  const [year, month] = key.split('-').map(Number)
  return new Date(year, month - 1, 1).toLocaleDateString('en-AU', { month: 'short', year: 'numeric' })
}
