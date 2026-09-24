import { Help } from './Help'

export function HouseholdSharingSummary({ context }: { context: 'invite' | 'connection' | 'settings' }) {
  return (
    <div className="household-sharing">
      <p>
        {context === 'connection'
          ? 'Everyone in your household will see this account and all of its transactions.'
          : 'Everyone in your household sees every account and every transaction, including accounts each person adds.'}
      </p>
      <Help title="What household members can do">
        <p>There are no private accounts. Adding someone gives them the whole picture, not a filtered view.</p>
        <ul>
          <li>Everyone can see all accounts, balances and transactions.</li>
          <li>Everyone can rename accounts, set budgets and tags, import statements and change transfer decisions.</li>
          <li>Everyone can connect a bank, but only the person who added a connection can disconnect it.</li>
          <li>Everyone can start or manage the subscription.</li>
          <li>Only the owner can invite or remove people.</li>
        </ul>
        <p>Removing someone does not disconnect the banks they connected, and once they are removed nobody can disconnect them here. Ask them to disconnect first, or revoke the consent with the bank.</p>
        <p>Grouping accounts for reporting changes what a chart counts. It does not restrict who can see them.</p>
      </Help>
    </div>
  )
}
