import { useMutation, useQuery } from '@tanstack/react-query'
import { CreditCard } from 'lucide-react'
import { useState } from 'react'
import { formatDate } from '../shared/formatters'
import { createCheckoutSession, createPortalSession, getBillingAccess } from './billingApi'

type BillingPlan = {
  key: string
  name: string
  cadence: string
  detail: string
}

const billingPlans: BillingPlan[] = [
  { key: 'Monthly', name: 'Monthly', cadence: 'Month to month', detail: 'Flexible access for early households.' },
  { key: 'Yearly', name: 'Yearly', cadence: 'Annual', detail: 'One yearly subscription for ongoing access.' },
]

export function BillingPage() {
  return (
    <section className="page billing-page">
      <header className="page-header">
        <div>
          <p>Subscription</p>
          <h1>Billing</h1>
        </div>
      </header>

      <BillingAccessPanel />
    </section>
  )
}

export function BillingAccessPanel({ compact = false }: { compact?: boolean }) {
  const [selectedPlan, setSelectedPlan] = useState(billingPlans[0]?.key ?? 'Monthly')
  const [message, setMessage] = useState<string | null>(null)
  const billingAccessQuery = useQuery({
    queryKey: ['billing-access'],
    queryFn: () => getBillingAccess(),
    staleTime: 30_000,
  })
  const checkoutMutation = useMutation({
    mutationFn: (plan: string) => createCheckoutSession(plan),
    onError: () => setMessage('Checkout is not available right now.'),
    onSuccess: x => {
      window.location.assign(x.url)
    },
  })
  const portalMutation = useMutation({
    mutationFn: () => createPortalSession(),
    onError: () => setMessage('Billing portal is not available yet.'),
    onSuccess: x => {
      window.location.assign(x.url)
    },
  })
  const access = billingAccessQuery.data
  const accessLabel = access?.hasAccess
    ? access.cancelAtPeriodEnd && access.currentPeriodEnd
      ? `Access active until ${formatDate(access.currentPeriodEnd)}`
      : 'Access active'
    : 'Subscription required'

  return (
    <section className={compact ? 'panel billing-panel billing-panel-locked' : 'panel billing-panel'}>
      <div className="panel-header">
        <div>
          <p>{accessLabel}</p>
          <h2>{access?.hasAccess ? 'Manage subscription' : 'Choose access'}</h2>
        </div>
        <CreditCard aria-hidden="true" />
      </div>

      <div className="billing-plan-grid">
        {billingPlans.map(x => (
          <button
            aria-pressed={selectedPlan === x.key}
            className={selectedPlan === x.key ? 'billing-plan is-selected' : 'billing-plan'}
            key={x.key}
            onClick={() => setSelectedPlan(x.key)}
            type="button"
          >
            <span>{x.name}</span>
            <strong>{x.cadence}</strong>
            <small>{x.detail}</small>
          </button>
        ))}
      </div>

      <div className="billing-actions">
        <button
          disabled={checkoutMutation.isPending}
          onClick={() => checkoutMutation.mutate(selectedPlan)}
          type="button"
        >
          {access?.hasAccess ? 'Change plan' : 'Start checkout'}
        </button>
        <button
          className="secondary-button"
          disabled={portalMutation.isPending}
          onClick={() => portalMutation.mutate()}
          type="button"
        >
          Manage billing
        </button>
        {message && <p>{message}</p>}
      </div>
    </section>
  )
}
