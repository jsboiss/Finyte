import { httpClient } from '../api/httpClient'
import type { OverviewResponse } from './types'

export function getOverviewQueryKey(accountId: string | null) {
  return ['overview', accountId ?? 'all'] as const
}

export async function getOverview(accountId: string | null) {
  const params = new URLSearchParams()
  if (accountId) {
    params.set('accountId', accountId)
  }

  return httpClient<OverviewResponse>({
    method: 'GET',
    url: `/api/overview${params.size > 0 ? `?${params}` : ''}`,
  })
}

export async function refreshOverview(accountId: string | null) {
  const params = new URLSearchParams()
  if (accountId) {
    params.set('accountId', accountId)
  }

  return httpClient<OverviewResponse>({
    method: 'POST',
    url: `/api/overview/refresh${params.size > 0 ? `?${params}` : ''}`,
  })
}
