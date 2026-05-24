import axios, { type AxiosRequestConfig } from 'axios'

const client = axios.create({
  baseURL: '/',
})

export async function httpClient<T>(config: AxiosRequestConfig): Promise<T> {
  const response = await client.request<T>(config)

  return response.data
}
