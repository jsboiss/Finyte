import { ClerkProvider } from '@clerk/react'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App.tsx'
import './index.css'

const publishableKey = import.meta.env.VITE_CLERK_PUBLISHABLE_KEY
const devAuthEnabled = import.meta.env.VITE_DEV_AUTH === 'true'

if (!devAuthEnabled && !publishableKey) {
  throw new Error('Missing VITE_CLERK_PUBLISHABLE_KEY')
}

const clerkAppearance = {
  variables: {
    colorBackground: '#18191b',
    colorInputBackground: '#222426',
    colorInputText: '#eeeeec',
    colorPrimary: '#c5c7c2',
    colorText: '#eeeeec',
    colorTextOnPrimaryBackground: '#111214',
    colorTextSecondary: '#a7aaa8',
    colorNeutral: '#36393d',
    borderRadius: '8px',
  },
  elements: {
    card: {
      backgroundColor: '#18191b',
      border: '1px solid #36393d',
      boxShadow: '0 24px 80px rgb(0 0 0 / 35%)',
    },
    footer: {
      backgroundColor: '#18191b',
      backgroundImage: 'none',
      borderTop: '1px solid #36393d',
    },
    formButtonPrimary: {
      boxShadow: 'none',
    },
    socialButtonsBlockButton: {
      backgroundColor: '#222426',
      borderColor: '#36393d',
      color: '#eeeeec',
    },
    userButtonPopoverCard: {
      backgroundColor: '#18191b',
      border: '1px solid #36393d',
      boxShadow: '0 24px 80px rgb(0 0 0 / 45%)',
    },
    userButtonPopoverActionButton: {
      color: '#eeeeec',
      '&:hover': {
        backgroundColor: '#222426',
        color: '#eeeeec',
      },
      '&:focus': {
        backgroundColor: '#222426',
        color: '#eeeeec',
      },
    },
    userButtonPopoverActionButtonIcon: {
      color: '#a7aaa8',
      '&:hover': {
        color: '#eeeeec',
      },
    },
    userButtonPopoverActionButtonText: {
      color: '#eeeeec',
      '&:hover': {
        color: '#eeeeec',
      },
    },
    userButtonPopoverFooter: {
      background: '#18191b',
      borderTop: '1px solid #36393d',
    },
    userPreviewMainIdentifier: {
      color: '#eeeeec',
    },
    userPreviewSecondaryIdentifier: {
      color: '#c5c7c2',
    },
  },
}

const app = devAuthEnabled
  ? <App />
  : (
    <ClerkProvider publishableKey={publishableKey} afterSignOutUrl="/" appearance={clerkAppearance}>
      <App />
    </ClerkProvider>
  )

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {app}
  </StrictMode>,
)
