import { useMutation } from '@tanstack/react-query'
import { useId, useRef, useState } from 'react'
import { exactAmount } from '../shared/formatters'
import type { TransactionTag } from '../transactions/types'
import { dayLabel, saveVendors, tagMerchant, tagPayment, toTagChoice, undoMerchantTag, undoPaymentTag, type HomeLoanMerchant, type HomeLoanPayment } from './homeLoanApi'

export type TagDone = (message: string, undo: () => Promise<unknown>) => void

export function TagOptions({ existing, starters }: { existing: TransactionTag[]; starters: string[] }) {
  return <>
    {existing.length > 0 && <optgroup label="Your tags">{existing.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</optgroup>}
    {starters.length > 0 && <optgroup label="New tag">{starters.map(x => <option key={x} value={`new:${x}`}>{x}</option>)}</optgroup>}
  </>
}

const titleCase = (text: string) => text.toLowerCase().replace(/(^|[\s-])(\p{L})/gu, (_, gap: string, letter: string) => gap + letter.toUpperCase())

export function MerchantRow({ merchant, currency, existing, starters, suggestedValue, onChanged, onDone }: {
  merchant: HomeLoanMerchant; currency: string; existing: TransactionTag[]; starters: string[]
  suggestedValue: (name: string) => string; onChanged: () => Promise<unknown>; onDone: TagDone
}) {
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(merchant.name)
  const [included, setIncluded] = useState(() => merchant.variants.map(x => x.ruleMerchantName))
  const [choice, setChoice] = useState(() => merchant.suggestedTag ? suggestedValue(merchant.suggestedTag) : '')
  const editButton = useRef<HTMLButtonElement>(null)
  const hintId = useId()
  const names = merchant.variants.map(x => x.ruleMerchantName)
  const all = useMutation({
    mutationFn: async (value: string) => {
      if (!merchant.confirmed) {
        await saveVendors(names.map(x => ({ statementName: x, vendor: merchant.name })))
      }
      return tagMerchant(names, toTagChoice(value))
    },
    onSuccess: async tag => {
      onDone(`Tagged ${merchant.count} ${merchant.count === 1 ? 'payment' : 'payments'} for ${merchant.name} as ${tag.name}.`, () => undoMerchantTag(names, tag.id))
      await onChanged()
    },
  })
  const vendor = useMutation({
    mutationFn: (assignments: { statementName: string; vendor: string | null }[]) => saveVendors(assignments),
    onSuccess: async () => { setEditing(false); await onChanged() },
  })
  const split = useMutation({
    mutationFn: async (variant: HomeLoanMerchant['variants'][number]) => {
      const vendorName = variant.ownName.toLowerCase() === merchant.name.toLowerCase() ? titleCase(variant.ruleMerchantName) : variant.ownName
      await saveVendors([{ statementName: variant.ruleMerchantName, vendor: vendorName }])
      return vendorName
    },
    onSuccess: async (vendorName, variant) => {
      onDone(`Moved ${variant.ruleMerchantName} out of ${merchant.name} into ${vendorName}.`,
        () => saveVendors([{ statementName: variant.ruleMerchantName, vendor: merchant.confirmed ? merchant.name : null }]))
      await onChanged()
    },
  })
  const busy = all.isPending || vendor.isPending || split.isPending
  const suggested = Boolean(merchant.suggestedTag) && choice === suggestedValue(merchant.suggestedTag ?? '')
  const money = (value: number) => exactAmount(value, currency)
  const closeEditor = () => {
    setEditing(false)
    setName(merchant.name)
    setIncluded(merchant.variants.map(x => x.ruleMerchantName))
    requestAnimationFrame(() => editButton.current?.focus())
  }

  return <li className="home-loan-merchant">
    <div className="home-loan-merchant-info">
      <strong>{merchant.name}{merchant.paymentTypes.map(type => <span key={type} className="home-loan-pill" data-type={type}>{type}</span>)}</strong>
      <span>{merchant.count} {merchant.count === 1 ? 'payment' : 'payments'} · {money(merchant.total)}</span>
      {!editing && <span className="home-loan-vendor-check">
        {merchant.confirmed ? 'Name confirmed.' : <>Is “{merchant.name}” the right name?
          <button type="button" className="home-loan-link" disabled={busy} onClick={() => vendor.mutate(names.map(x => ({ statementName: x, vendor: merchant.name })))}>Yes<span className="visually-hidden">, {merchant.name} is right</span></button></>}
        <button ref={editButton} type="button" className="home-loan-link" disabled={busy} onClick={() => setEditing(true)}>{merchant.confirmed ? 'Rename or split' : 'Edit'}<span className="visually-hidden"> {merchant.name}</span></button>
      </span>}
      {editing && <form className="home-loan-vendor-form" onSubmit={event => {
        event.preventDefault()
        vendor.mutate(merchant.variants.map(x => ({ statementName: x.ruleMerchantName, vendor: included.includes(x.ruleMerchantName) ? name.trim() : x.ownName })))
      }}>
        <label>Vendor name<input autoFocus required maxLength={120} value={name} onChange={event => setName(event.target.value)} /></label>
        {merchant.variants.length > 1 && <fieldset><legend>Statement names that belong to {name.trim() || 'this vendor'}</legend>
          {merchant.variants.map(x => <label key={x.ruleMerchantName} className="home-loan-check">
            <input type="checkbox" checked={included.includes(x.ruleMerchantName)}
              onChange={event => setIncluded(current => event.target.checked ? [...current, x.ruleMerchantName] : current.filter(y => y !== x.ruleMerchantName))} />
            <span>{x.ruleMerchantName} <small>{x.count} · {money(x.total)}</small></span>
          </label>)}
          <small>Unticked names become their own vendor.</small>
        </fieldset>}
        <div className="home-loan-actions">
          <button type="submit" disabled={busy || !name.trim() || included.length === 0}>Save name</button>
          <button type="button" className="home-loan-secondary" onClick={closeEditor}>Cancel</button>
        </div>
      </form>}
    </div>
    <form className="home-loan-apply" onSubmit={event => { event.preventDefault(); if (choice) { all.mutate(choice) } }}>
      <select aria-label={`Tag for all ${merchant.name} payments`} aria-describedby={suggested ? hintId : undefined} value={choice} onChange={event => setChoice(event.target.value)}>
        <option value="">Choose a tag</option>
        <TagOptions existing={existing} starters={starters} />
      </select>
      <button type="submit" disabled={busy || !choice}>{all.isPending ? 'Tagging…' : 'Tag all'}</button>
      <small id={hintId} className="home-loan-hint">{suggested ? 'Suggested from the name' : ''}</small>
    </form>
    {merchant.variants.length > 1 && !editing && <details className="home-loan-variants" open={!merchant.confirmed}>
      <summary>Grouped from {merchant.variants.length} statement names<span className="visually-hidden"> for {merchant.name}</span></summary>
      <ul>{merchant.variants.map(x => <li key={x.ruleMerchantName}>
        <span>{x.ruleMerchantName}</span><small>{x.count} {x.count === 1 ? 'payment' : 'payments'} · {money(x.total)}</small>
        <button type="button" className="home-loan-link" disabled={busy} onClick={() => split.mutate(x)}>Not {merchant.name}<span className="visually-hidden">: move {x.ruleMerchantName} out</span></button>
      </li>)}</ul>
    </details>}
    <details className="home-loan-payments">
      <summary>Show payments<span className="visually-hidden"> for {merchant.name}</span> and tag them one by one</summary>
      <ul>{merchant.payments.map(payment => <PaymentTagRow key={payment.transactionId} payment={payment} merchant={merchant.name} currency={currency}
        existing={existing} starters={starters} onChanged={onChanged} onDone={onDone} />)}</ul>
    </details>
    {(all.isError || vendor.isError || split.isError) && <p role="alert">That change could not be saved. Try again.</p>}
  </li>
}

function PaymentTagRow({ payment, merchant, currency, existing, starters, onChanged, onDone }: {
  payment: HomeLoanPayment; merchant: string; currency: string; existing: TransactionTag[]; starters: string[]
  onChanged: () => Promise<unknown>; onDone: TagDone
}) {
  const [choice, setChoice] = useState('')
  const money = exactAmount(payment.amount, currency)
  const tag = useMutation({
    mutationFn: (value: string) => tagPayment(payment.transactionId, toTagChoice(value)),
    onSuccess: async applied => {
      onDone(`Tagged the ${dayLabel(payment.date)} ${merchant} payment (${money}) as ${applied.name}.`, () => undoPaymentTag(payment.transactionId, applied.id))
      await onChanged()
    },
  })
  return <li>
    <span>{dayLabel(payment.date)} · {payment.account}</span><span>{payment.description}</span><strong>{money}</strong>
    <form className="home-loan-apply" onSubmit={event => { event.preventDefault(); if (choice) { tag.mutate(choice) } }}>
      <select aria-label={`Tag for the ${dayLabel(payment.date)} ${merchant} payment of ${money}`} value={choice} onChange={event => setChoice(event.target.value)}>
        <option value="">Tag this payment</option>
        <TagOptions existing={existing} starters={starters} />
      </select>
      <button type="submit" className="home-loan-secondary" disabled={tag.isPending || !choice}>Apply</button>
    </form>
  </li>
}
