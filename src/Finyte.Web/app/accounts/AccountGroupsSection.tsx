import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { isAxiosError } from 'axios'
import { useState } from 'react'
import { Drawer } from '../shared/Drawer'
import { deleteAccountGroup, getAccountGroups, saveAccountGroup, type AccountGroup } from './accountGroupsApi'
import type { Account } from './accountsApi'

type Draft = { id?: string; name: string; accountIds: string[] }

export function AccountGroupsSection({ accounts }: { accounts: Account[] }) {
  const client = useQueryClient()
  const groups = useQuery({ queryKey: ['account-groups'], queryFn: getAccountGroups })
  const [draft, setDraft] = useState<Draft | null>(null)
  const refresh = () => Promise.all(['account-groups', 'overview', 'cash-flow'].map(key => client.invalidateQueries({ queryKey: [key] })))
  const save = useMutation({ mutationFn: saveAccountGroup, onSuccess: async () => { setDraft(null); await refresh() } })
  const remove = useMutation({ mutationFn: deleteAccountGroup, onSuccess: refresh })
  const names = (group: AccountGroup) => group.accountIds.map(id => accounts.find(x => x.id === id)?.name).filter(Boolean).join(', ')

  return <section className="panel account-groups">
    <div className="account-groups-heading">
      <div><h2>Groups</h2><p>Save a set of accounts, such as shared spending, to view together on the dashboard. Groups only change what you look at, not who can see an account.</p></div>
      <button type="button" onClick={() => { save.reset(); setDraft({ name: '', accountIds: [] }) }}>New group</button>
    </div>
    {groups.isError && <p role="alert">Unable to load groups.</p>}
    {remove.isError && <p role="alert">That group could not be deleted.</p>}
    {groups.data?.length === 0 && <p className="empty-inline">No groups yet.</p>}
    <ul className="account-group-list">{groups.data?.map(group => <li key={group.id}>
      <div><strong>{group.name}</strong><small>{group.accountIds.length} {group.accountIds.length === 1 ? 'account' : 'accounts'} · {names(group)}</small></div>
      <div className="account-group-actions">
        <Link to="/" search={{ scope: `group:${group.id}` }}>View</Link>
        <button type="button" className="secondary" onClick={() => { save.reset(); setDraft({ id: group.id, name: group.name, accountIds: group.accountIds }) }}>Edit</button>
        <button type="button" className="secondary" disabled={remove.isPending} onClick={() => remove.mutate(group.id)}>Delete</button>
      </div>
    </li>)}</ul>
    {draft && <Drawer title={draft.id ? 'Edit group' : 'New group'} onClose={() => setDraft(null)}>
      <form className="account-group-form" onSubmit={event => { event.preventDefault(); save.mutate(draft) }}>
        <label>Name<input required maxLength={80} value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} placeholder="Shared spending" /></label>
        <fieldset><legend>Accounts</legend>
          {accounts.map(account => <label className="transaction-filter-checkbox" key={account.id}>
            <input type="checkbox" checked={draft.accountIds.includes(account.id)} onChange={event => setDraft({ ...draft, accountIds: event.target.checked ? [...draft.accountIds, account.id] : draft.accountIds.filter(x => x !== account.id) })} />
            <span>{account.name}</span>
          </label>)}
        </fieldset>
        {save.isError && <p role="alert">{errorMessage(save.error)}</p>}
        <button type="submit" disabled={save.isPending || !draft.name.trim() || draft.accountIds.length === 0}>{draft.id ? 'Save group' : 'Create group'}</button>
      </form>
    </Drawer>}
  </section>
}

function errorMessage(error: Error) {
  if (isAxiosError(error) && typeof error.response?.data === 'string') {
    return error.response.data
  }
  return 'The group could not be saved. Try again.'
}
