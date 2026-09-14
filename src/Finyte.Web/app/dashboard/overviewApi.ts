import { httpClient } from '../api/httpClient'
import type { OverviewResponse } from './types'

export function getOverviewQueryKey(accountId: string | null, includeInternalTransfers = false) {
  return ['overview', accountId ?? 'all', includeInternalTransfers] as const
}

export async function getOverview(accountId: string | null, includeInternalTransfers = false) {
  const params = new URLSearchParams({ includeInternalTransfers: String(includeInternalTransfers) })
  if (accountId) {
    params.set('accountId', accountId)
  }

  const response = await httpClient<OverviewResponse>({
    method: 'GET',
    url: `/api/overview${params.size > 0 ? `?${params}` : ''}`,
  })
  return { ...response, includeInternalTransfers }
}

export async function refreshOverview(accountId: string | null, includeInternalTransfers = false) {
  const params = new URLSearchParams({ includeInternalTransfers: String(includeInternalTransfers) })
  if (accountId) {
    params.set('accountId', accountId)
  }

  const response = await httpClient<OverviewResponse>({
    method: 'POST',
    url: `/api/overview/refresh${params.size > 0 ? `?${params}` : ''}`,
  })
  return { ...response, includeInternalTransfers }
}
