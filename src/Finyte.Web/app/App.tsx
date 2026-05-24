import { SignIn, UserButton, useAuth } from '@clerk/react'
import { createRootRoute, createRoute, createRouter, Link, Outlet, RouterProvider } from '@tanstack/react-router'
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { createColumnHelper, flexRender, getCoreRowModel, useReactTable } from '@tanstack/react-table'
import { Activity, Banknote, CalendarClock, Home, Settings } from 'lucide-react'
import { getAppStatus } from './api/generated/finyteApi'
import { AuthTokenProvider } from './auth/AuthTokenProvider'
import './App.css'

type DashboardItem = {
  name: string
  status: string
  nextStep: string
}

const dashboardItems: DashboardItem[] = [
  { name: 'Banking', status: 'Not connected', nextStep: 'Connect Fiskil sandbox' },
  { name: 'Income', status: 'Waiting on banking data', nextStep: 'Enable after transaction sync' },
  { name: 'Bills', status: 'Empty', nextStep: 'Detect recurring payments' },
]

const columnHelper = createColumnHelper<DashboardItem>()

const columns = [
  columnHelper.accessor('name', {
    header: 'Area',
    cell: x => x.getValue(),
  }),
  columnHelper.accessor('status', {
    header: 'Status',
    cell: x => x.getValue(),
  }),
  columnHelper.accessor('nextStep', {
    header: 'Next step',
    cell: x => x.getValue(),
  }),
]

function DashboardShell() {
  const { isLoaded, isSignedIn } = useAuth()

  if (!isLoaded) {
    return (
      <div className="auth-page">
        <div className="auth-loading">Loading</div>
      </div>
    )
  }

  if (!isSignedIn) {
    return <SignInPage />
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <Banknote aria-hidden="true" />
          <span>Finyte</span>
        </div>
        <nav>
          <Link to="/" activeProps={{ className: 'active' }}>
            <Home aria-hidden="true" />
            Dashboard
          </Link>
          <Link to="/connections" activeProps={{ className: 'active' }}>
            <Activity aria-hidden="true" />
            Connections
          </Link>
          <Link to="/settings" activeProps={{ className: 'active' }}>
            <Settings aria-hidden="true" />
            Settings
          </Link>
        </nav>
        <AuthControls />
      </aside>
      <main>
        <Outlet />
      </main>
    </div>
  )
}

function AuthControls() {
  return (
    <div className="auth-panel">
      <UserButton />
    </div>
  )
}

function SignInPage() {
  return (
    <main className="auth-page">
      <SignIn />
    </main>
  )
}

function DashboardPage() {
  const statusQuery = useQuery({
    queryKey: ['app-status'],
    queryFn: () => getAppStatus(),
  })

  const table = useReactTable({
    data: dashboardItems,
    columns,
    getCoreRowModel: getCoreRowModel(),
  })

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <p>Household dashboard</p>
          <h1>Financial overview</h1>
        </div>
        <div className="status-pill">
          <CalendarClock aria-hidden="true" />
          {statusQuery.data?.databaseAvailable ? 'API connected' : 'Waiting for API'}
        </div>
      </header>

      <div className="metric-grid">
        <div>
          <span>Accounts</span>
          <strong>0</strong>
        </div>
        <div>
          <span>Transactions</span>
          <strong>0</strong>
        </div>
        <div>
          <span>Upcoming bills</span>
          <strong>0</strong>
        </div>
      </div>

      <section className="table-section">
        <h2>Setup progress</h2>
        <table>
          <thead>
            {table.getHeaderGroups().map(x => (
              <tr key={x.id}>
                {x.headers.map(y => (
                  <th key={y.id}>{flexRender(y.column.columnDef.header, y.getContext())}</th>
                ))}
              </tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.map(x => (
              <tr key={x.id}>
                {x.getVisibleCells().map(y => (
                  <td key={y.id}>{flexRender(y.column.columnDef.cell, y.getContext())}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </section>
  )
}

function ConnectionsPage() {
  return (
    <section className="page">
      <header className="page-header">
        <div>
          <p>Provider access</p>
          <h1>Connections</h1>
        </div>
      </header>
      <button type="button">Connect bank</button>
    </section>
  )
}

function SettingsPage() {
  return (
    <section className="page">
      <header className="page-header">
        <div>
          <p>Account</p>
          <h1>Settings</h1>
        </div>
      </header>
      <button type="button">Manage security</button>
    </section>
  )
}

const rootRoute = createRootRoute({ component: DashboardShell })
const indexRoute = createRoute({ getParentRoute: () => rootRoute, path: '/', component: DashboardPage })
const connectionsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/connections', component: ConnectionsPage })
const settingsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/settings', component: SettingsPage })
const routeTree = rootRoute.addChildren([indexRoute, connectionsRoute, settingsRoute])
const router = createRouter({ routeTree })
const queryClient = new QueryClient()

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthTokenProvider>
        <RouterProvider router={router} />
      </AuthTokenProvider>
    </QueryClientProvider>
  )
}
