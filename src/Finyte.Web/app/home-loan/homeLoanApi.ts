import { httpClient } from '../api/httpClient'

export type HomeLoanMonth = {
  month: string; firstApplicantIncome: number; secondApplicantIncome: number; otherIncome: number; unlabelledCredits: number
  refunds: number; gifts: number; livingExpenses: number; commitments: number; saved: number; surplus: number
}
export type HomeLoanPayment = { transactionId: string; date: string; account: string; description: string; amount: number }
export type HomeLoanMerchant = {
  key: string; name: string; confirmed: boolean; paymentTypes: string[]; suggestedTag: string | null; count: number; total: number
  variants: { ruleMerchantName: string; ownName: string; count: number; total: number }[]; payments: HomeLoanPayment[]
}
export type HomeLoanAccountRole = { accountId: string; name: string; type: string; role: 'spending' | 'savings' | 'excluded' }
export type HomeLoanReport = {
  currency: string; months: number; fromMonth: string; toMonth: string; availableMonths: string[]
  setup: { firstApplicant: string; secondApplicant: string | null; accounts: HomeLoanAccountRole[]; version: number; saved: boolean }
  monthlyTotals: HomeLoanMonth[]; average: HomeLoanMonth; total: HomeLoanMonth
  categories: { key: string; label: string; group: 'living' | 'commitment' | 'savings' | 'excluded'; total: number; monthlyAverage: number; byMonth: number[] }[]
  tags: { tagId: string; name: string; color: string; category: string; isDefault: boolean; total: number }[]
  payers: { key: string; name: string; paymentType: string | null; kind: string | null; applicant: number | null; count: number; total: number; monthsSeen: number; latestDate: string; payments: HomeLoanPayment[] }[]
  needsLabel: HomeLoanMerchant[]
  flags: { kind: string; label: string; advice: string; count: number; total: number; items: { date: string; account: string; description: string; amount: number }[] }[]
  gaps: { accountId: string; accountName: string; missingMonths: string[] }[]
}

export const lenderCategories = [
  { key: 'food-groceries', label: 'Food & groceries', group: 'living' },
  { key: 'residence', label: 'Rates, utilities & home maintenance', group: 'living' },
  { key: 'phone-internet-media', label: 'Phone, internet & media', group: 'living' },
  { key: 'transport', label: 'Transport', group: 'living' },
  { key: 'clothing-personal-care', label: 'Clothing & personal care', group: 'living' },
  { key: 'recreation', label: 'Recreation & holidays', group: 'living' },
  { key: 'medical', label: 'Medical & health', group: 'living' },
  { key: 'general-insurance', label: 'Insurance (home, contents, car)', group: 'living' },
  { key: 'life-insurance', label: 'Life, accident & illness insurance', group: 'living' },
  { key: 'childcare-education', label: 'Childcare, public education & dependants', group: 'living' },
  { key: 'private-school', label: 'Private school fees', group: 'living' },
  { key: 'strata', label: 'Strata / body corporate', group: 'living' },
  { key: 'other-living', label: 'Other living expenses', group: 'living' },
  { key: 'rent', label: 'Rent', group: 'commitment' },
  { key: 'loan-repayments', label: 'Loan repayments', group: 'commitment' },
  { key: 'credit-card', label: 'Credit card repayments', group: 'commitment' },
  { key: 'bnpl', label: 'Buy now pay later', group: 'commitment' },
  { key: 'child-support', label: 'Child support', group: 'commitment' },
  { key: 'savings-elsewhere', label: 'Moved to savings (another account)', group: 'savings' },
  { key: 'not-expense', label: 'Not an expense', group: 'excluded' },
] as const

export const getHomeLoan = (months: number, end: string) =>
  httpClient<HomeLoanReport>({ method: 'GET', url: '/api/home-loan', params: { months, end: end || undefined } })

export const saveSetup = (body: { firstApplicant: string; secondApplicant: string | null; accounts: { accountId: string; role: string }[]; expectedVersion: number }) =>
  httpClient<{ version: number }>({ method: 'PUT', url: '/api/home-loan/setup', data: body })

export const saveIncomeSource = (body: { key: string; name: string; kind: string | null; applicant: number | null }) =>
  httpClient<void>({ method: 'PUT', url: '/api/home-loan/income-sources', data: body })

export const saveCategory = (body: { tagId: string; category: string | null }) =>
  httpClient<void>({ method: 'PUT', url: '/api/home-loan/categories', data: body })

export type TagChoice = { tagId: string } | { tagName: string }

export function toTagChoice(value: string): TagChoice {
  return value.startsWith('new:') ? { tagName: value.slice(4) } : { tagId: value }
}

export type AppliedTag = { id: string; name: string }

export const tagMerchant = (statementNames: string[], tag: TagChoice) =>
  httpClient<AppliedTag>({ method: 'PUT', url: '/api/home-loan/merchant-tags', data: { statementNames, ...tag } })

export const undoMerchantTag = (statementNames: string[], tagId: string) =>
  httpClient<void>({ method: 'PUT', url: '/api/home-loan/merchant-tags/undo', data: { statementNames, tagId } })

export const tagPayment = (transactionId: string, tag: TagChoice) =>
  httpClient<AppliedTag>({ method: 'PUT', url: '/api/home-loan/payment-tag', data: { transactionId, ...tag } })

export const undoPaymentTag = (transactionId: string, tagId: string) =>
  httpClient<void>({ method: 'PUT', url: '/api/home-loan/payment-tag/undo', data: { transactionId, tagId } })

export const saveVendors = (assignments: { statementName: string; vendor: string | null }[]) =>
  httpClient<void>({ method: 'PUT', url: '/api/merchant-vendors', data: { assignments } })

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

export function monthLabel(key: string) {
  const [year, month] = key.split('-').map(Number)
  return new Date(year, month - 1, 1).toLocaleDateString('en-AU', { month: 'short', year: 'numeric' })
}
