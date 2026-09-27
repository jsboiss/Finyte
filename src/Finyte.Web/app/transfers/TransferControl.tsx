import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ArrowRightLeft } from '../shared/Icons'
import { OptionsMenu, OptionsMenuHeading, OptionsMenuItem, OptionsMenuNote } from '../shared/OptionsMenu'
import { reviewTransfer, transferQueryKeys, transferSourceLabel, type TransferAction } from './transfersApi'

type TransferAccount = { id: string; name: string }
type TransferTransaction = {
  id: string
  accountId: string
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
  return (
    <OptionsMenu label="Transaction options" align={align}>
      <TransferMenuItems transaction={transaction} accounts={accounts} />
    </OptionsMenu>
  )
}

function TransferMenuItems({ transaction, accounts }: { transaction: TransferTransaction; accounts: TransferAccount[] }) {
  const queryClient = useQueryClient()
  const [error, setError] = useState('')
  const review = useMutation({
    mutationFn: ({ action, counterpartyAccountId }: { action: TransferAction; counterpartyAccountId?: string }) => reviewTransfer(transaction.id, action, counterpartyAccountId),
    onSuccess: async () => { setError(''); await Promise.all(transferQueryKeys.map(x => queryClient.invalidateQueries({ queryKey: [x] }))) },
    onError: () => setError('Unable to update this transaction.'),
  })
  const others = accounts.filter(x => x.id !== transaction.accountId)
  const act = (action: TransferAction, counterpartyAccountId?: string) => review.mutate({ action, counterpartyAccountId })

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

  return <>
    <OptionsMenuHeading>Mark as transfer {direction(transaction)}</OptionsMenuHeading>
    {others.length === 0 && <OptionsMenuNote>Add another account first.</OptionsMenuNote>}
    {others.map(x => <OptionsMenuItem key={x.id} disabled={review.isPending} onSelect={() => act('mark', x.id)}>{x.name}</OptionsMenuItem>)}
    {error && <OptionsMenuNote role="alert">{error}</OptionsMenuNote>}
  </>
}

function direction(transaction: TransferTransaction) {
  return transaction.amountMinorUnits < 0 ? 'to' : 'from'
}
