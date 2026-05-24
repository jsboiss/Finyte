import axios, { type AxiosRequestConfig } from 'axios'

let clerkToken: (() => Promise<string | null>) | undefined

const client = axios.create({
  baseURL: '/',
})

export function setAuthTokenProvider(provider: () => Promise<string | null>) {
  clerkToken = provider
}

export async function httpClient<T>(config: AxiosRequestConfig): Promise<T> {
  const token = clerkToken ? await clerkToken() : null
  const response = await client.request<T>({
    ...config,
    headers: {
      ...config.headers,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  })

  return response.data
}
