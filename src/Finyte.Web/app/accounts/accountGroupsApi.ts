import { httpClient } from '../api/httpClient'

export type AccountGroup = { id: string; name: string; accountIds: string[] }

export const getAccountGroups = () => httpClient<AccountGroup[]>({ url: '/api/account-groups' })

export function saveAccountGroup(group: { id?: string; name: string; accountIds: string[] }) {
  return httpClient<AccountGroup>({
    method: group.id ? 'PUT' : 'POST',
    url: group.id ? `/api/account-groups/${group.id}` : '/api/account-groups',
    data: { name: group.name, accountIds: group.accountIds },
  })
}

export const deleteAccountGroup = (id: string) => httpClient<void>({ method: 'DELETE', url: `/api/account-groups/${id}` })
