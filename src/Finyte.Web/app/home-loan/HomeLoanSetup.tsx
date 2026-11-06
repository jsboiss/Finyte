import { useMutation } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import { useState } from 'react'
import { accountTypeLabel } from '../accounts/accountsApi'
import { saveSetup, type HomeLoanReport } from './homeLoanApi'

const roleOptions = [['spending', 'Spending'], ['savings', 'Savings'], ['excluded', 'Not included']] as const

export function HomeLoanSetup({ report, onSaved, onCancel }: { report: HomeLoanReport; onSaved: () => Promise<void>; onCancel: () => void }) {
  const [first, setFirst] = useState(report.setup.firstApplicant)
  const [second, setSecond] = useState(report.setup.secondApplicant ?? '')
  const [roles, setRoles] = useState(() => Object.fromEntries(report.setup.accounts.map(x => [x.accountId, x.role as string])))
  const save = useMutation({
    mutationFn: () => saveSetup({
      firstApplicant: first.trim(), secondApplicant: second.trim() || null, expectedVersion: report.setup.version,
      accounts: report.setup.accounts.map(x => ({ accountId: x.accountId, role: roles[x.accountId] })),
    }),
    onSuccess: onSaved,
  })

  return <form className="home-loan-setup" onSubmit={event => { event.preventDefault(); save.mutate() }}>
    <fieldset>
      <legend>Who's applying</legend>
      <div className="home-loan-fields">
        <label>First applicant<input required maxLength={60} value={first} onChange={event => setFirst(event.target.value)} /></label>
        <label>Second applicant (optional)<input maxLength={60} value={second} onChange={event => setSecond(event.target.value)} placeholder="Leave blank if applying alone" /></label>
      </div>
    </fieldset>
    <fieldset>
      <legend>What each account is for</legend>
      <p>Spending accounts count towards income and expenses. Savings accounts show how much you saved: everything that went in, less what came out. Transfers between included accounts are never counted as income or spending. Lenders usually want statements for every account, so include them all if you can.</p>
      <div className="home-loan-roles">
        {report.setup.accounts.map(account => <div className="home-loan-role" key={account.accountId}>
          <span><strong>{account.name}</strong><small>{accountTypeLabel(account.type)}</small></span>
          <select aria-label={`Role for ${account.name}`} value={roles[account.accountId]} onChange={event => setRoles(x => ({ ...x, [account.accountId]: event.target.value }))}>
            {roleOptions.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        </div>)}
      </div>
    </fieldset>
    {save.isError && <p role="alert">{isAxiosError(save.error) && typeof save.error.response?.data === 'string' ? save.error.response.data : 'The setup could not be saved. Try again.'}</p>}
    <div className="home-loan-actions">
      <button type="submit" disabled={save.isPending || !first.trim()}>Save setup</button>
      <button type="button" className="home-loan-secondary" onClick={onCancel}>Cancel</button>
    </div>
  </form>
}
