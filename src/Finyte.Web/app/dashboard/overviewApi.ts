import { httpClient } from '../api/httpClient'
import { allAccountsScope, appendScope } from './accountScope'
import type { OverviewResponse } from './types'

export function getOverviewQueryKey(scope: string, includeInternalTransfers = false) {
  return ['overview', scope || allAccountsScope, includeInternalTransfers] as const
}

export async function getOverview(scope: string, includeInternalTransfers = false) {
  const params = appendScope(new URLSearchParams({ includeInternalTransfers: String(includeInternalTransfers) }), scope)
  const response = await httpClient<OverviewResponse>({
    method: 'GET',
    url: `/api/overview?${params}`,
  })
  return { ...response, includeInternalTransfers }
}

export async function refreshOverview(scope: string, includeInternalTransfers = false) {
  const params = appendScope(new URLSearchParams({ includeInternalTransfers: String(includeInternalTransfers) }), scope)
  const response = await httpClient<OverviewResponse>({
    method: 'POST',
    url: `/api/overview/refresh?${params}`,
  })
  return { ...response, includeInternalTransfers }
}