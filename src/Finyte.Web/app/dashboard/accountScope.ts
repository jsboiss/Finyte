import type { OverviewResponse } from './types'

export const allAccountsScope = 'all'

export function appendScope(params: URLSearchParams, scope: string) {
  if (scope.startsWith('group:')) {
    params.set('groupId', scope.slice(6))
  } else if (scope.startsWith('set:')) {
    for (const id of scope.slice(4).split(',').filter(Boolean)) {
      params.append('accountIds', id)
    }
  } else if (scope && scope !== allAccountsScope) {
    params.set('accountId', scope)
  }
  return params
}

export function scopeOf(overview: OverviewResponse) {
  if (overview.scope.accountIds?.length) {
    return `set:${overview.scope.accountIds.join(',')}`
  }
  return overview.scope.accountId ?? allAccountsScope
}

export function scopeAccountIds(scope: string): string[] | null {
  if (scope.startsWith('set:')) {
    return scope.slice(4).split(',').filter(Boolean)
  }
  return scope && scope !== allAccountsScope && !scope.startsWith('group:') ? [scope] : null
}
