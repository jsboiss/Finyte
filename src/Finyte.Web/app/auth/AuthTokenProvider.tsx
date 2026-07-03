import { useAuth } from '@clerk/react'
import type { ReactNode } from 'react'
import { useEffect } from 'react'
import { setAuthTokenProvider } from '../api/httpClient'

type AuthTokenProviderProps = {
  children: ReactNode
}

export function AuthTokenProvider({ children }: AuthTokenProviderProps) {
  return import.meta.env.VITE_DEV_AUTH === 'true'
    ? <>{children}</>
    : <ClerkAuthTokenProvider>{children}</ClerkAuthTokenProvider>
}

function ClerkAuthTokenProvider({ children }: AuthTokenProviderProps) {
  const { getToken } = useAuth()

  useEffect(() => {
    setAuthTokenProvider(() => getToken())
  }, [getToken])

  return children
}
