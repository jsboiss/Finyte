import axios, { type AxiosRequestConfig } from 'axios'

let clerkToken: (() => Promise<string | null>) | undefined
const devIdentityStorageKey = 'finyte-dev-identity'

export type DevIdentity = {
  userId: string
  organizationId: string
  role: string
}

const client = axios.create({
  baseURL: '/',
})

export function setAuthTokenProvider(provider: () => Promise<string | null>) {
  clerkToken = provider
}

export function getDevIdentity(): DevIdentity {
  const storedIdentity = localStorage.getItem(devIdentityStorageKey)
  return storedIdentity
    ? JSON.parse(storedIdentity) as DevIdentity
    : { userId: 'dev-user', organizationId: 'org_dev-family', role: 'org:admin' }
}

export function setDevIdentity(identity: DevIdentity) {
  localStorage.setItem(devIdentityStorageKey, JSON.stringify(identity))
}

export async function httpClient<T>(config: AxiosRequestConfig): Promise<T> {
  const token = clerkToken ? await clerkToken() : null
  const devIdentity = import.meta.env.VITE_DEV_AUTH === 'true' ? getDevIdentity() : undefined
  const response = await client.request<T>({
    ...config,
    headers: {
      ...config.headers,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(devIdentity ? {
        'X-Dev-Organization': devIdentity.organizationId,
        'X-Dev-Role': devIdentity.role,
        'X-Dev-User': devIdentity.userId,
      } : {}),
    },
  })

  return response.data
}
