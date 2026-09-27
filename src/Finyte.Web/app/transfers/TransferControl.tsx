import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Drawer } from '../shared/Drawer'
import { ArrowRightLeft } from '../shared/Icons'
import { OptionsMenu, OptionsMenuHeading, OptionsMenuItem, OptionsMenuNote } from '../shared/OptionsMenu'
import { reviewTransfer, transferQueryKeys, transferSourceLabel, type TransferAction } from './transfersApi'

type TransferAccount = { id: string; name: string }
type TransferTransaction = {
  id: string
  accountId: string
  description?: string | null
  amountMinorUnits: number
  isInternalTransfer?: boolean
  internalTransferAccountName?: string | null
  internalTransferSource?: string | null
}

export function TransferBadge({ transaction }: { transaction: TransferTransaction }) {
  if (transaction.isInternalTransfer) {
    const other = transaction.internalTransferAccountName ?? 'another account'
    return (
      <OptionsMenu label={`Internal transfer ${direction(transaction)} ${other}`} trigger={<><ArrowRightLeft size={12} /><span>{other}</span></>} className="transfer-indicator">
        <OptionsMenuHeading>Internal transfer</OptionsMenuHeading>
        <OptionsMenuNote>{direction(transaction) === 'to' ? 'Sent to' : 'Received from'} {other}</OptionsMenuNote>
        <OptionsMenuNote>{transferSourceLabel(transaction.internalTransferSource)}</OptionsMenuNote>
        <OptionsMenuNote>Left out of spending and income. Balances are unchanged.</OptionsMenuNote>
      </OptionsMenu>
    )
  }
  if (transaction.internalTransferSource === 'excluded') {
    return (
      <OptionsMenu label="Marked as not a transfer" trigger={<><ArrowRightLeft size={12} /><span>Not a transfer</span></>} className="transfer-indicator is-excluded">
        <OptionsMenuHeading>Not a transfer</OptionsMenuHeading>
        <OptionsMenuNote>Marked by you. It counts in spending or income and detection will not change it.</OptionsMenuNote>
      </OptionsMenu>
    )
  }
  return null
}

export function TransactionOptions({ transaction, accounts, align }: { transaction: TransferTransaction; accounts: TransferAccount[]; align?: 'left' | 'right' }) {
  const [marking, setMarking] = useState(false)
  return <>
    <OptionsMenu label="Transaction options" align={align}>
      <TransferMenuItems transaction={transaction} onMark={() => setMarking(true)} />
    </OptionsMenu>
    {marking && <Drawer title="Internal transfer" onClose={() => setMarking(false)}><MarkTransferForm transaction={transaction} accounts={accounts} onDone={() => setMarking(false)} /></Drawer>}
  </>
}

function useReview(transactionId: string) {
  const queryClient = useQueryClient()
  const [error, setError] = useState('')
  const review = useMutation({
    mutationFn: ({ action, counterpartyAccountId, createRule }: { action: TransferAction; counterpartyAccountId?: string; createRule?: boolean }) => reviewTransfer(transactionId, action, counterpartyAccountId, createRule),
    onSuccess: async () => { setError(''); await Promise.all(['accounts', ...transferQueryKeys].map(x => queryClient.invalidateQueries({ queryKey: [x] }))) },
    onError: () => setError('Unable to update this transaction.'),
  })
  return { review, error }
}

function TransferMenuItems({ transaction, onMark }: { transaction: TransferTransaction; onMark: () => void }) {
  const { review, error } = useReview(transaction.id)
  const act = (action: TransferAction) => review.mutate({ action })

  if (transaction.isInternalTransfer) {
    return <>
      <OptionsMenuHeading>{transferSourceLabel(transaction.internalTransferSource)}</OptionsMenuHeading>
      <OptionsMenuItem disabled={review.isPending} onSelect={() => act('exclude')}>Not a transfer</OptionsMenuItem>
      {transaction.internalTransferSource === 'manual' && <OptionsMenuItem disabled={review.isPending} onSelect={() => act('reset')}>Reset to detected</OptionsMenuItem>}
      {error && <OptionsMenuNote role="alert">{error}</OptionsMenuNote>}
    </>
  }

  if (transaction.internalTransferSource === 'excluded') {
    return <>
      <OptionsMenuHeading>{transferSourceLabel('excluded')}</OptionsMenuHeading>
      <OptionsMenuItem disabled={review.isPending} onSelect={() => act('reset')}>Undo</OptionsMenuItem>
      {error && <OptionsMenuNote role="alert">{error}</OptionsMenuNote>}
    </>
  }

  return <OptionsMenuItem onSelect={onMark}>Mark as internal transfer</OptionsMenuItem>
}

function MarkTransferForm({ transaction, accounts, onDone }: { transaction: TransferTransaction; accounts: TransferAccount[]; onDone: () => void }) {
  const { review, error } = useReview(transaction.id)
  const others = accounts.filter(x => x.id !== transaction.accountId)
  const [accountId, setAccountId] = useState(others[0]?.id ?? '')
  const [createRule, setCreateRule] = useState(true)
  return <form className="mark-transfer-form" onSubmit={event => { event.preventDefault(); if (accountId) { review.mutate({ action: 'mark', counterpartyAccountId: accountId, createRule }, { onSuccess: onDone }) } }}>
    <p className="mark-transfer-description">{transaction.description}</p>
    {others.length === 0 && <p>Add another account first.</p>}
    <label>{direction(transaction) === 'to' ? 'Sent to' : 'Received from'}<select value={accountId} onChange={event => setAccountId(event.target.value)} disabled={review.isPending || others.length === 0}>{others.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
    <label className="mark-transfer-rule"><input type="checkbox" checked={createRule} onChange={event => setCreateRule(event.target.checked)} disabled={review.isPending} />Label similar future transactions as internal</label>
    <p className="mark-transfer-note">Internal transfers are left out of spending and income. Balances are unchanged.</p>
    <div className="mark-transfer-actions"><button type="submit" disabled={review.isPending || !accountId}>{review.isPending ? 'Saving…' : 'Confirm'}</button><button type="button" className="secondary-button" disabled={review.isPending} onClick={onDone}>Cancel</button></div>
    {error && <p role="alert">{error}</p>}
  </form>
}

function direction(transaction: TransferTransaction) {
  return transaction.amountMinorUnits < 0 ? 'to' : 'from'
}
