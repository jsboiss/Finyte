import { httpClient } from '../api/httpClient'

export type BillingAccessResponse = {
  hasAccess: boolean
  status: string | null
  stripePriceId: string | null
  currentPeriodEnd: string | null
  cancelAtPeriodEnd: boolean
}

export type CheckoutSessionResponse = {
  url: string
}

export type PortalSessionResponse = {
  url: string
}

export async function createCheckoutSession(plan: string) {
  return httpClient<CheckoutSessionResponse>({
    data: { plan },
    headers: { 'Content-Type': 'application/json' },
    method: 'POST',
    url: '/api/billing/checkout-session',
  })
}

export async function createPortalSession() {
  return httpClient<PortalSessionResponse>({
    method: 'POST',
    url: '/api/billing/portal-session',
  })
}

export async function getBillingAccess() {
  return httpClient<BillingAccessResponse>({
    method: 'GET',
    url: '/api/billing/access',
  })
}
