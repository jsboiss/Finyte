import { CreateOrganization, OrganizationSwitcher, SignIn, UserButton, useAuth, useOrganization } from '@clerk/react'
import { link, type LinkError } from '@fiskil/link'
import { keepPreviousData, QueryClient, QueryClientProvider, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createRootRoute, createRoute, createRouter, Link, Outlet, RouterProvider, useRouterState } from '@tanstack/react-router'
import { createColumnHelper, flexRender, getCoreRowModel, getFilteredRowModel, type ColumnFiltersState, useReactTable } from '@tanstack/react-table'
import { Activity, Banknote, CreditCard, Home, Mail, Menu, Plus, ReceiptText, Settings, Shield, SlidersHorizontal, Tags, Trash2, UserPlus, Users, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { getAccounts, getAppStatus } from './api/generated/finyteApi'
import { getDevIdentity, httpClient, setDevIdentity } from './api/httpClient'
import { AuthTokenProvider } from './auth/AuthTokenProvider'
import { BillingPage } from './billing/BillingAccessPanel'
import { DashboardPage } from './dashboard/DashboardPage'
import { ImportsPage } from './imports/ImportsPage'
import { TransfersPage } from './transfers/TransfersPage'
import { TransactionAccountChip, TransactionAmount } from './transactions/TransactionCard'
import { TransactionCardList } from './transactions/TransactionCardList'
import { TransactionPagination } from './transactions/TransactionPagination'
import type { Transaction, TransactionPage, TransactionTag } from './transactions/types'
import './App.css'

type MerchantTagRule = {
  id: string
  merchantName: string
  tag: TransactionTag
}

type DateFilter = {
  from?: string
  to?: string
}

type AmountFilter = {
  min?: string
  max?: string
}

type CreateTagInput = {
  name: string
  color: string
}

type SetTransactionTagsInput = {
  transactionId: string
  tagIds: string[]
  manualTagIds?: string[]
}

type ProviderConnection = {
  id: string
  provider: string
  institutionId?: string
  status: string
  isOwnedByCurrentMember: boolean
  createdAt: string
  updatedAt: string
}

type FamilyMember = {
  id: string
  userId: string
  displayName?: string
  email?: string
  role: 'Owner' | 'Member'
  isCurrent: boolean
}

type FamilyInvitation = {
  id: string
  email: string
  role: string
  status: string
  createdAt: string
}

type Family = {
  id: string
  organizationId: string
  name: string
  canManage: boolean
  isDevelopment: boolean
  members: FamilyMember[]
  invitations: FamilyInvitation[]
}

type CreateMerchantRuleInput = {
  ruleId?: string
  merchantName: string
  tagId: string
}

const queryClient = new QueryClient()
const devAuthEnabled = import.meta.env.VITE_DEV_AUTH === 'true'
const tagColorOptions = ['#bae6fd', '#bbf7d0', '#fde68a', '#fecdd3', '#ddd6fe', '#fed7aa', '#ccfbf1', '#e9d5ff']
const transactionsPageSize = 25
const transactionColumnHelper = createColumnHelper<Transaction>()

function DashboardShell() {
  if (devAuthEnabled) {
    return <SignedInShell />
  }

  return <ClerkDashboardShell />
}

function ClerkDashboardShell() {
  const { isLoaded, isSignedIn, orgId } = useAuth()

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

  if (!orgId) {
    return (
      <main className="auth-page">
        <CreateOrganization afterCreateOrganizationUrl="/" />
      </main>
    )
  }

  return <FamilyProvisioner />
}

function FamilyProvisioner() {
  const { organization } = useOrganization()
  const currentUserQuery = useQuery({
    queryKey: ['current-user', organization?.id],
    queryFn: getCurrentUser,
  })
  const {
    error: provisionFamilyError,
    isIdle: isProvisionFamilyIdle,
    isPending: isProvisioningFamily,
    mutate: provisionCurrentFamily,
  } = useMutation({
    mutationFn: () => provisionFamily(organization?.name ?? 'My family'),
    onSuccess: currentUser => queryClient.setQueryData(['current-user', organization?.id], currentUser),
  })

  useEffect(() => {
    if (currentUserQuery.data && !currentUserQuery.data.onboarding.hasFamily && isProvisionFamilyIdle) {
      provisionCurrentFamily()
    }
  }, [currentUserQuery.data, isProvisionFamilyIdle, provisionCurrentFamily])

  if (currentUserQuery.isLoading || isProvisioningFamily || !currentUserQuery.data?.onboarding.hasFamily) {
    return (
      <main className="auth-page">
        <div className="auth-loading">
          {currentUserQuery.isError || provisionFamilyError ? 'Family setup failed. Refresh to try again.' : 'Setting up your family'}
        </div>
      </main>
    )
  }

  return <SignedInShell />
}

function SignedInShell() {
  const [isMenuOpen, setIsMenuOpen] = useState(false)
  const closeMenu = useCallback(() => setIsMenuOpen(false), [])
  const pathname = useRouterState({ select: x => x.location.pathname })
  const statusQuery = useQuery({
    queryKey: ['app-status'],
    queryFn: () => getAppStatus(),
    staleTime: 60_000,
  })
  const isApiConnected = statusQuery.data?.databaseAvailable === true

  return (
    <div className="app-shell">
      <header className="mobile-header">
        <div className="brand">
          <div className={isApiConnected ? 'logo-placeholder is-connected' : 'logo-placeholder is-disconnected'} aria-hidden="true" />
          <span>{getPageTitle(pathname)}</span>
        </div>
        <button
          aria-controls="app-sidebar"
          aria-expanded={isMenuOpen}
          aria-label="Open navigation menu"
          className="mobile-menu-button"
          onClick={() => setIsMenuOpen(true)}
          type="button"
        >
          <Menu aria-hidden="true" className="mobile-menu-icon" size={32} strokeWidth={2.6} />
        </button>
      </header>

      {isMenuOpen && <button aria-label="Close navigation menu" className="nav-backdrop" onClick={closeMenu} type="button" />}

      <aside className={isMenuOpen ? 'sidebar is-open' : 'sidebar'} id="app-sidebar">
        <div className="sidebar-header">
          <div className="brand">
            <Banknote aria-hidden="true" />
            <span>Finyte</span>
          </div>
        </div>
        <nav>
          <AppNavLinks onNavigate={closeMenu} />
        </nav>
        <AuthControls />
      </aside>
      <main>
        <Outlet />
      </main>
    </div>
  )
}

function AppNavLinks({ onNavigate }: { onNavigate?: () => void }) {
  return (
    <>
      <Link to="/" activeProps={{ className: 'active' }} onClick={onNavigate}>
        <Home aria-hidden="true" />
        Dashboard
      </Link>
      <Link to="/transactions" activeProps={{ className: 'active' }} onClick={onNavigate}>
        <ReceiptText aria-hidden="true" />
        Transactions
      </Link>
      <Link to="/connections" activeProps={{ className: 'active' }} onClick={onNavigate}>
        <Activity aria-hidden="true" />
        Connections
      </Link>
      <Link to="/billing" activeProps={{ className: 'active' }} onClick={onNavigate}>
        <CreditCard aria-hidden="true" />
        Billing
      </Link>
      <Link to="/imports" activeProps={{ className: 'active' }} onClick={onNavigate}>
        <ReceiptText aria-hidden="true" />
        Imports
      </Link>
      <Link to="/transfers" activeProps={{ className: 'active' }} onClick={onNavigate}>
        <Activity aria-hidden="true" />
        Transfers
      </Link>
      <Link to="/settings" activeProps={{ className: 'active' }} onClick={onNavigate}>
        <Settings aria-hidden="true" />
        Settings
      </Link>
    </>
  )
}

function getPageTitle(pathname: string) {
  if (pathname.startsWith('/transfers')) {
    return 'Transfers'
  }
  if (pathname.startsWith('/imports')) {
    return 'Imports'
  }
  if (pathname.startsWith('/connections')) {
    return 'Connections'
  }

  if (pathname.startsWith('/transactions')) {
    return 'Transactions'
  }

  if (pathname.startsWith('/billing')) {
    return 'Billing'
  }

  if (pathname.startsWith('/settings')) {
    return 'Settings'
  }

  return 'Dashboard'
}

function AuthControls() {
  if (devAuthEnabled) {
    return (
      <div className="auth-panel dev-auth-panel">
        <span>Dev User</span>
      </div>
    )
  }

  return (
    <div className="auth-panel">
      <OrganizationSwitcher hidePersonal />
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

type CurrentUser = {
  userId?: string
  tenantId?: string
  role?: string
  onboarding: {
    hasFamily: boolean
  }
}

async function getCurrentUser() {
  return httpClient<CurrentUser>({
    method: 'GET',
    url: '/api/auth/me',
  })
}

async function provisionFamily(name: string) {
  return httpClient<CurrentUser>({
    data: { name },
    headers: { 'Content-Type': 'application/json' },
    method: 'POST',
    url: '/api/auth/family',
  })
}

function TransactionsPage() {
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([])
  const [showFilters, setShowFilters] = useState(false)
  const [showTagManagement, setShowTagManagement] = useState(false)
  const [tagName, setTagName] = useState('')
  const [tagColor, setTagColor] = useState('#64748b')
  const [merchantName, setMerchantName] = useState('')
  const [merchantTagId, setMerchantTagId] = useState('')
  const [editingMerchantRuleId, setEditingMerchantRuleId] = useState<string>()
  const [transactionPage, setTransactionPage] = useState(1)
  const queryClient = useQueryClient()
  const transactionsQuery = useQuery({
    queryKey: ['transactions', transactionPage],
    queryFn: () => getTransactions(transactionPage, transactionsPageSize),
    placeholderData: keepPreviousData,
  })
  const accountsQuery = useQuery({
    queryKey: ['accounts'],
    queryFn: () => getAccounts(),
    staleTime: 60_000,
  })
  const tagsQuery = useQuery({
    queryKey: ['tags'],
    queryFn: () => getTags(),
  })
  const merchantRulesQuery = useQuery({
    enabled: showTagManagement,
    queryKey: ['merchant-tags'],
    queryFn: () => getMerchantRules(),
  })
  const createTagMutation = useMutation({
    mutationFn: (input: CreateTagInput) => createTag(input),
    onMutate: async input => {
      await queryClient.cancelQueries({ queryKey: ['tags'] })
      const previousTags = queryClient.getQueryData<TransactionTag[]>(['tags'])
      const optimisticTag = { id: `pending-${crypto.randomUUID()}`, name: input.name, color: input.color }
      queryClient.setQueryData<TransactionTag[]>(['tags'], x => [...(x ?? []), optimisticTag])
      setTagName('')
      return { optimisticTagId: optimisticTag.id, previousTags }
    },
    onError: (_error, _input, context) => {
      queryClient.setQueryData(['tags'], context?.previousTags)
    },
    onSuccess: (tag, _input, context) => {
      queryClient.setQueryData<TransactionTag[]>(['tags'], x => uniqueTagsById((x ?? []).map(y => y.id === context.optimisticTagId ? tag : y)))
    },
  })
  const deleteTagMutation = useMutation({
    mutationFn: (tagId: string) => deleteTag(tagId),
    onMutate: async tagId => {
      await queryClient.cancelQueries({ queryKey: ['tags'] })
      await queryClient.cancelQueries({ queryKey: ['transactions'] })
      await queryClient.cancelQueries({ queryKey: ['merchant-tags'] })
      const previousTags = queryClient.getQueryData<TransactionTag[]>(['tags'])
      const previousTransactions = queryClient.getQueryData<TransactionPage>(['transactions', transactionPage])
      const previousMerchantRules = queryClient.getQueryData<MerchantTagRule[]>(['merchant-tags'])
      queryClient.setQueryData<TransactionTag[]>(['tags'], x => (x ?? []).filter(y => y.id !== tagId))
      queryClient.setQueryData<TransactionPage>(['transactions', transactionPage], x => x ? { ...x, items: x.items.map(y => ({ ...y, tags: y.tags.filter(z => z.id !== tagId) })) } : x)
      queryClient.setQueryData<MerchantTagRule[]>(['merchant-tags'], x => (x ?? []).filter(y => y.tag.id !== tagId))
      return { previousTags, previousTransactions, previousMerchantRules }
    },
    onError: (_error, _tagId, context) => {
      queryClient.setQueryData(['tags'], context?.previousTags)
      queryClient.setQueryData(['transactions', transactionPage], context?.previousTransactions)
      queryClient.setQueryData(['merchant-tags'], context?.previousMerchantRules)
    },
  })
  const updateTransactionTagsMutation = useMutation({
    mutationFn: (input: SetTransactionTagsInput) => setTransactionTags(input),
    onMutate: async input => {
      await queryClient.cancelQueries({ queryKey: ['transactions'] })
      const previousTransactions = queryClient.getQueryData<TransactionPage>(['transactions', transactionPage])
      const allTags = queryClient.getQueryData<TransactionTag[]>(['tags']) ?? []
      const transaction = previousTransactions?.items.find(x => x.id === input.transactionId)
      const nextTags = allTags.filter(x => input.tagIds.includes(x.id)).map(x => {
        const previous = transaction?.tags.find(y => y.id === x.id)
        return input.manualTagIds?.includes(x.id) || !previous ? { ...x, source: 'manual' as const } : previous
      })
      queryClient.setQueryData<TransactionPage>(['transactions', transactionPage], x => x ? { ...x, items: x.items.map(y => y.id === input.transactionId ? { ...y, tags: nextTags } : y) } : x)
      return { previousTransactions }
    },
    onError: (_error, _input, context) => {
      queryClient.setQueryData(['transactions', transactionPage], context?.previousTransactions)
    },
    onSuccess: async (nextTags, input) => {
      queryClient.setQueryData<TransactionPage>(['transactions', transactionPage], x => x ? { ...x, items: x.items.map(y => y.id === input.transactionId ? { ...y, tags: nextTags } : y) } : x)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['overview'] }),
        queryClient.invalidateQueries({ queryKey: ['transactions'] }),
      ])
    },
  })
  const setTransactionTagIds = useCallback((transactionId: string, tagIds: string[], manualTagIds?: string[]) => {
    updateTransactionTagsMutation.mutate({ transactionId, tagIds, manualTagIds })
  }, [updateTransactionTagsMutation])
  const restoreAutomaticTagsMutation = useMutation({
    mutationFn: (transactionId: string) => httpClient<TransactionTag[]>({
      method: 'POST', url: `/api/transactions/${transactionId}/tags/restore-automatic`,
    }),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['transactions'] }),
        queryClient.invalidateQueries({ queryKey: ['overview'] }),
      ])
    },
  })
  const createMerchantRuleMutation = useMutation({
    mutationFn: (input: CreateMerchantRuleInput) => createMerchantRule(input),
    onSuccess: async () => {
      setMerchantName('')
      setEditingMerchantRuleId(undefined)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['merchant-tags'] }),
        queryClient.invalidateQueries({ queryKey: ['transactions'] }),
        queryClient.invalidateQueries({ queryKey: ['overview'] }),
      ])
    },
  })
  const deleteMerchantRuleMutation = useMutation({
    mutationFn: (ruleId: string) => deleteMerchantRule(ruleId),
    onMutate: async ruleId => {
      await queryClient.cancelQueries({ queryKey: ['merchant-tags'] })
      const previousMerchantRules = queryClient.getQueryData<MerchantTagRule[]>(['merchant-tags'])
      queryClient.setQueryData<MerchantTagRule[]>(['merchant-tags'], x => (x ?? []).filter(y => y.id !== ruleId))
      return { previousMerchantRules }
    },
    onError: (_error, _ruleId, context) => {
      queryClient.setQueryData(['merchant-tags'], context?.previousMerchantRules)
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['transactions'] }),
        queryClient.invalidateQueries({ queryKey: ['overview'] }),
      ])
    },
  })
  const columns = useMemo(() => [
    transactionColumnHelper.accessor('postedDate', {
      header: 'Date',
      filterFn: (x, y, z: DateFilter) => {
        const value = x.getValue<string>(y)
        return (!z.from || value >= z.from) && (!z.to || value <= z.to)
      },
    }),
    transactionColumnHelper.accessor('accountId', {
      header: 'Account',
      cell: x => (
        <TransactionAccountChip accountId={x.getValue()}>{x.row.original.accountDisplayName}</TransactionAccountChip>
      ),
      filterFn: (x, y, z: string) => x.getValue<string>(y) === z,
    }),
    transactionColumnHelper.accessor('description', {
      header: 'Description',
      cell: x => (
        <div className="transaction-description">
          <strong>{x.getValue()}</strong>
          {x.row.original.isInternalTransfer && <Link className="transfer-badge" to="/transfers" search={{ view: 'confirmed' }}>Internal transfer · excluded from totals</Link>}
          <span>{x.row.original.merchantName ?? ''}</span>
        </div>
      ),
      filterFn: 'includesString',
    }),
    transactionColumnHelper.accessor('category', {
      header: 'Category',
      filterFn: 'includesString',
    }),
    transactionColumnHelper.accessor('tags', {
      header: 'Tags',
      cell: x => (
        <TagEditor
          allTags={tagsQuery.data ?? []}
          selectedTags={x.row.original.tags}
          excludedTagIds={x.row.original.automaticTagExclusions}
          onChange={(y, z) => setTransactionTagIds(x.row.original.id, y, z)}
          onRestore={() => restoreAutomaticTagsMutation.mutate(x.row.original.id)}
          disabled={updateTransactionTagsMutation.isPending || restoreAutomaticTagsMutation.isPending}
        />
      ),
      filterFn: (x, y, z: string) => x.getValue<TransactionTag[]>(y).some(a => a.name.toLowerCase().includes(z.toLowerCase())),
    }),
    transactionColumnHelper.accessor('amountMinorUnits', {
      header: 'Amount',
      cell: x => <TransactionAmount amountMinorUnits={x.getValue()} currencyCode={x.row.original.currency} />,
      filterFn: (x, y, z: AmountFilter) => {
        const value = x.getValue<number>(y) / 100
        const min = z.min ? Number(z.min) : null
        const max = z.max ? Number(z.max) : null
        return (min == null || value >= min) && (max == null || value <= max)
      },
    }),
  ], [setTransactionTagIds, tagsQuery.data, restoreAutomaticTagsMutation, updateTransactionTagsMutation.isPending])
  // TanStack Table intentionally returns stateful functions that React Compiler cannot memoize.
  // eslint-disable-next-line react-hooks/incompatible-library
  const table = useReactTable({
    data: transactionsQuery.data?.items ?? [],
    columns,
    state: { columnFilters, columnVisibility: { category: false } },
    onColumnFiltersChange: setColumnFilters,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
  })
  const hasFilters = columnFilters.length > 0
  const isLoading = transactionsQuery.isLoading || transactionsQuery.isFetching
  const totalTransactions = transactionsQuery.data?.totalCount ?? 0
  const totalPages = Math.max(Math.ceil(totalTransactions / transactionsPageSize), 1)
  const visibleTransactions = table.getRowModel().rows.map(x => x.original)

  return (
    <section className="page transactions-page">
      <header className="page-header transactions-header">
        <div>
          <p>Ledger</p>
          <h1>Transactions</h1>
        </div>
        <div className="transactions-actions">
          <button className={showTagManagement ? 'secondary-button is-active' : 'secondary-button'} onClick={() => setShowTagManagement(x => !x)} type="button">
            <Tags aria-hidden="true" />
            Tags
          </button>
          <button className={showFilters ? 'secondary-button is-active' : 'secondary-button'} onClick={() => setShowFilters(x => !x)} type="button">
            <SlidersHorizontal aria-hidden="true" />
            Filters
          </button>
          <button className="secondary-button" disabled={!hasFilters} onClick={() => table.resetColumnFilters()} type="button">
            <X aria-hidden="true" />
            Clear
          </button>
        </div>
      </header>

      {(updateTransactionTagsMutation.isError || restoreAutomaticTagsMutation.isError || createMerchantRuleMutation.isError || deleteMerchantRuleMutation.isError) && (
        <p role="alert">Could not save the tag change. Check the merchant name and tag, then try again.</p>
      )}

      {showTagManagement && (
        <section className="panel tag-management-panel">
          <div className="tag-management-column">
            <div className="panel-header compact-panel-header">
              <div>
                <p>Labels</p>
                <h2>Tags</h2>
              </div>
            </div>
            <div className="tag-form">
              <input onChange={x => setTagName(x.target.value)} placeholder="New tag" value={tagName} />
              <div className="tag-color-picker">
                {tagColorOptions.map(x => (
                  <button
                    aria-label={`Use tag color ${x}`}
                    className={tagColor.toLowerCase() === x ? 'is-selected' : ''}
                    key={x}
                    onClick={() => setTagColor(x)}
                    style={{ backgroundColor: x }}
                    type="button"
                  />
                ))}
                <input aria-label="Custom tag color" onChange={x => setTagColor(x.target.value)} type="color" value={tagColor} />
              </div>
              <button disabled={!tagName.trim() || createTagMutation.isPending} onClick={() => createTagMutation.mutate({ name: tagName.trim(), color: tagColor })} type="button">
                <Plus aria-hidden="true" />
                Add
              </button>
            </div>
            <div className="tag-pill-list">
              {(tagsQuery.data ?? []).map(x => (
                <span className="editable-tag-pill" key={x.id}>
                  <TagPill tag={x} />
                  <button aria-label={`Delete tag ${x.name}`} onClick={() => deleteTagMutation.mutate(x.id)} type="button">
                    <Trash2 aria-hidden="true" />
                  </button>
                </span>
              ))}
              {!tagsQuery.isLoading && (tagsQuery.data?.length ?? 0) === 0 && <span className="empty-inline">No tags yet.</span>}
            </div>
          </div>
          <div className="tag-management-column merchant-rules-column">
            <div className="panel-header compact-panel-header">
              <div>
                <p>Automation</p>
                <h2>Merchant tag rules</h2>
              </div>
            </div>
            <p className="tag-rule-help">Rules apply to existing and future transactions from every source. Matching ignores case and punctuation, and matches whole words at the start of the merchant name (or description when no merchant is supplied). For example, “Coles” includes “Coles 4568”.</p>
            <p className="tag-rule-help">Editing or deleting a rule updates automatic tags. Manual tags and tags with an unknown original source stay. Removing a tag from one transaction prevents a rule from adding it back.</p>
            <div className="merchant-rule-form">
              <input aria-label="Merchant rule name" maxLength={256} onChange={x => setMerchantName(x.target.value)} placeholder="Merchant name" value={merchantName} />
              <select aria-label="Merchant rule tag" onChange={x => setMerchantTagId(x.target.value)} value={merchantTagId}>
                <option value="">Select tag</option>
                {(tagsQuery.data ?? []).map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
              </select>
              <button disabled={!merchantName.trim() || !merchantTagId || createMerchantRuleMutation.isPending} onClick={() => createMerchantRuleMutation.mutate({ ruleId: editingMerchantRuleId, merchantName: merchantName.trim(), tagId: merchantTagId })} type="button">
                <Plus aria-hidden="true" />
                {editingMerchantRuleId ? 'Save rule' : 'Add rule'}
              </button>
              {editingMerchantRuleId && <button onClick={() => { setEditingMerchantRuleId(undefined); setMerchantName('') }} type="button">Cancel</button>}
            </div>
            <div className="tag-pill-list">
              {(merchantRulesQuery.data ?? []).map(x => (
                <span className="merchant-rule-pill" key={x.id}>
                  {x.merchantName}
                  <TagPill tag={x.tag} />
                  <button aria-label={`Edit rule for ${x.merchantName}`} onClick={() => { setEditingMerchantRuleId(x.id); setMerchantName(x.merchantName); setMerchantTagId(x.tag.id) }} type="button">Edit</button>
                  <button aria-label={`Delete rule for ${x.merchantName}`} disabled={deleteMerchantRuleMutation.isPending} onClick={() => deleteMerchantRuleMutation.mutate(x.id)} type="button">
                    <Trash2 aria-hidden="true" />
                  </button>
                </span>
              ))}
              {!merchantRulesQuery.isLoading && (merchantRulesQuery.data?.length ?? 0) === 0 && <span className="empty-inline">No merchant rules yet.</span>}
            </div>
          </div>
        </section>
      )}

      {showFilters && (
        <section className="panel transaction-filters-panel">
          <FilterField className="date-filter-field" label="Date">
            <DateRangeFilter
              value={(table.getColumn('postedDate')?.getFilterValue() as DateFilter | undefined) ?? {}}
              onChange={x => table.getColumn('postedDate')?.setFilterValue(x.from || x.to ? x : undefined)}
            />
          </FilterField>
          <FilterField label="Account">
            <select
              disabled={accountsQuery.isLoading}
              onChange={x => table.getColumn('accountId')?.setFilterValue(x.target.value || undefined)}
              value={(table.getColumn('accountId')?.getFilterValue() as string | undefined) ?? ''}
            >
              <option value="">All accounts</option>
              {(accountsQuery.data ?? []).map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </FilterField>
          <FilterField label="Description">
            <DebouncedFilterInput
              onChange={x => table.getColumn('description')?.setFilterValue(x)}
              placeholder="Search descriptions"
              value={(table.getColumn('description')?.getFilterValue() as string | undefined) ?? ''}
            />
          </FilterField>
          <FilterField label="Category">
            <DebouncedFilterInput
              onChange={x => table.getColumn('category')?.setFilterValue(x)}
              placeholder="Search categories"
              value={(table.getColumn('category')?.getFilterValue() as string | undefined) ?? ''}
            />
          </FilterField>
          <FilterField label="Tags">
            <DebouncedFilterInput
              onChange={x => table.getColumn('tags')?.setFilterValue(x)}
              placeholder="Search tags"
              value={(table.getColumn('tags')?.getFilterValue() as string | undefined) ?? ''}
            />
          </FilterField>
          <FilterField className="amount-filter-field" label="Amount">
            <AmountRangeFilter
              value={(table.getColumn('amountMinorUnits')?.getFilterValue() as AmountFilter | undefined) ?? {}}
              onChange={x => table.getColumn('amountMinorUnits')?.setFilterValue(x.min || x.max ? x : undefined)}
            />
          </FilterField>
        </section>
      )}

      <TransactionCardList
        emptyMessage={hasFilters ? 'No transactions match the current filters.' : 'No transactions imported yet.'}
        isLoading={isLoading}
        renderTags={x => (
          <TagEditor
            allTags={tagsQuery.data ?? []}
            selectedTags={x.tags}
            excludedTagIds={x.automaticTagExclusions}
            onChange={(y, z) => setTransactionTagIds(x.id, y, z)}
            onRestore={() => restoreAutomaticTagsMutation.mutate(x.id)}
            disabled={updateTransactionTagsMutation.isPending || restoreAutomaticTagsMutation.isPending}
          />
        )}
        transactions={visibleTransactions}
      />

      <section className="transaction-table-section">
        <div className={isLoading ? 'table-progress is-visible' : 'table-progress'} />
        <table>
          <thead>
            {table.getHeaderGroups().map(x => (
              <tr key={x.id}>{x.headers.map(y => <th key={y.id}>{flexRender(y.column.columnDef.header, y.getContext())}</th>)}</tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.map(x => (
              <tr key={x.id}>{x.getVisibleCells().map(y => <td key={y.id}>{flexRender(y.column.columnDef.cell, y.getContext())}</td>)}</tr>
            ))}
            {table.getRowModel().rows.length === 0 && (
              <tr>
                <td className="empty-table-cell" colSpan={5}>{hasFilters ? 'No transactions match the current filters.' : 'No transactions imported yet.'}</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>

      <TransactionPagination
        isLoading={isLoading}
        onNext={() => setTransactionPage(x => x + 1)}
        onPrevious={() => setTransactionPage(x => Math.max(1, x - 1))}
        page={transactionPage}
        totalCount={totalTransactions}
        totalPages={totalPages}
        visibleCount={visibleTransactions.length}
      />
    </section>
  )
}

function FilterField({ label, children, className }: { label: string; children: ReactNode; className?: string }) {
  return (
    <label className={className ? `filter-field ${className}` : 'filter-field'}>
      <span>{label}</span>
      {children}
    </label>
  )
}

function DebouncedFilterInput({ value, onChange, placeholder }: { value: string; onChange: (value: string) => void; placeholder: string }) {
  return <DebouncedFilterInputDraft initialValue={value} key={value} onChange={onChange} placeholder={placeholder} />
}

function DebouncedFilterInputDraft({ initialValue, onChange, placeholder }: { initialValue: string; onChange: (value: string) => void; placeholder: string }) {
  const [draftValue, setDraftValue] = useState(initialValue)
  const onChangeRef = useRef(onChange)

  useEffect(() => {
    onChangeRef.current = onChange
  }, [onChange])

  useEffect(() => {
    if (draftValue === initialValue) {
      return
    }

    const timeout = window.setTimeout(() => onChangeRef.current(draftValue), 150)
    return () => window.clearTimeout(timeout)
  }, [draftValue, initialValue])

  return <input onChange={x => setDraftValue(x.target.value)} placeholder={placeholder} value={draftValue} />
}

function DateRangeFilter({ value, onChange }: { value: DateFilter; onChange: (value: DateFilter) => void }) {
  return (
    <div className="range-filter">
      <input onChange={x => onChange({ ...value, from: x.target.value })} type="date" value={value.from ?? ''} />
      <input onChange={x => onChange({ ...value, to: x.target.value })} type="date" value={value.to ?? ''} />
    </div>
  )
}

function AmountRangeFilter({ value, onChange }: { value: AmountFilter; onChange: (value: AmountFilter) => void }) {
  return (
    <div className="range-filter">
      <input onChange={x => onChange({ ...value, min: x.target.value })} placeholder="Min" type="number" value={value.min ?? ''} />
      <input onChange={x => onChange({ ...value, max: x.target.value })} placeholder="Max" type="number" value={value.max ?? ''} />
    </div>
  )
}

function TagEditor({ allTags, selectedTags, excludedTagIds = [], onChange, onRestore, disabled }: {
  allTags: TransactionTag[]
  selectedTags: TransactionTag[]
  excludedTagIds?: string[]
  onChange: (tagIds: string[], manualTagIds?: string[]) => void
  onRestore: () => void
  disabled: boolean
}) {
  const [isOpen, setIsOpen] = useState(false)
  const [popupPosition, setPopupPosition] = useState<{ left: number; maxHeight: number; placement: 'above' | 'below'; top: number }>({ left: 0, maxHeight: 280, placement: 'below', top: 0 })
  const containerRef = useRef<HTMLDivElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const selectedIds = new Set(selectedTags.map(x => x.id))
  const visibleSelectedTags = allTags.filter(x => selectedIds.has(x.id)).map(x => selectedTags.find(y => y.id === x.id) ?? x)

  const updatePopupPosition = useCallback(() => {
    const rect = buttonRef.current?.getBoundingClientRect()
    if (!rect) {
      return
    }

    const viewportGap = 8
    const triggerGap = 4
    const popupMinWidth = Math.min(320, window.innerWidth - 16)
    const preferredMaxHeight = 400
    const viewportHeight = window.innerHeight
    const viewportWidth = window.innerWidth
    const availableBelow = viewportHeight - rect.bottom - viewportGap - triggerGap
    const availableAbove = rect.top - viewportGap - triggerGap
    const placement = availableBelow >= preferredMaxHeight || availableBelow >= availableAbove ? 'below' : 'above'
    const availableHeight = placement === 'below' ? availableBelow : availableAbove
    const maxHeight = Math.max(0, Math.min(preferredMaxHeight, availableHeight))
    const left = Math.min(Math.max(viewportGap, rect.left), Math.max(viewportGap, viewportWidth - popupMinWidth - viewportGap))
    const top = placement === 'below' ? rect.bottom + triggerGap : rect.top - triggerGap

    setPopupPosition({ left, maxHeight, placement, top })
  }, [])

  useEffect(() => {
    if (!isOpen) {
      return
    }

    function closeOnOutsideClick(event: MouseEvent) {
      if (!containerRef.current?.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }

    document.addEventListener('mousedown', closeOnOutsideClick)
    return () => document.removeEventListener('mousedown', closeOnOutsideClick)
  }, [isOpen])

  useEffect(() => {
    if (!isOpen) {
      return
    }

    updatePopupPosition()
    window.addEventListener('resize', updatePopupPosition)
    window.addEventListener('scroll', updatePopupPosition, true)
    return () => {
      window.removeEventListener('resize', updatePopupPosition)
      window.removeEventListener('scroll', updatePopupPosition, true)
    }
  }, [isOpen, updatePopupPosition])

  if (allTags.length === 0) {
    return <span className="empty-inline">No tags</span>
  }

  return (
    <div className="tag-editor" ref={containerRef}>
      <button
        aria-label="Edit transaction tags"
        disabled={disabled}
        onClick={() => {
          updatePopupPosition()
          setIsOpen(true)
        }}
        ref={buttonRef}
        type="button"
      >
        {visibleSelectedTags.length > 0 ? visibleSelectedTags.map(x => <TagPill key={x.id} tag={x} />) : <Plus aria-hidden="true" />}
      </button>
      {isOpen && (
        <div
          className="tag-menu"
          style={{
            left: popupPosition.left,
            maxHeight: popupPosition.maxHeight,
            top: popupPosition.top,
            transform: popupPosition.placement === 'above' ? 'translateY(-100%)' : undefined,
          }}
        >
          <p className="tag-rule-help">Unchecking a tag keeps it removed during future syncs. Use “Keep manual” to retain an automatic tag when its rule changes.</p>
          {allTags.map(x => (
            <div className="tag-menu-option" key={x.id}>
              <label>
                <input
                  checked={selectedIds.has(x.id)}
                  disabled={disabled}
                  onChange={y => {
                    const nextIds = y.target.checked ? [...selectedIds, x.id] : [...selectedIds].filter(z => z !== x.id)
                    onChange(nextIds)
                  }}
                  type="checkbox"
                />
                <TagPill tag={selectedTags.find(y => y.id === x.id) ?? x} />
              </label>
              {excludedTagIds.includes(x.id) && <small>Removed from rules</small>}
              {selectedIds.has(x.id) && selectedTags.some(y => y.id === x.id && (y.source === 'merchant-rule' || y.source === 'legacy')) && (
                <button disabled={disabled} onClick={() => onChange([...selectedIds], [x.id])} type="button">Keep manual</button>
              )}
            </div>
          ))}
          <button disabled={disabled || excludedTagIds.length === 0} onClick={() => { setIsOpen(false); onRestore() }} type="button">Restore removed automatic tags</button>
          <p className="tag-rule-help">Restoring keeps manual tags and allows current and future merchant rules to add removed tags again.</p>
        </div>
      )}
    </div>
  )
}

function TagPill({ tag }: { tag: TransactionTag }) {
  const sourceLabel = tag.source === 'merchant-rule' ? 'Auto' : tag.source === 'legacy' ? 'Unknown' : tag.source === 'manual' ? 'Manual' : tag.source === 'system' ? 'System' : null
  const sourceDescription = tag.source === 'merchant-rule' ? `Automatically applied by merchant rule: ${tag.merchantRuleName ?? 'matching merchant'}`
    : tag.source === 'legacy' ? 'Added before source tracking. Preserved when merchant rules change.'
    : tag.source === 'manual' ? 'Manually chosen. Preserved when merchant rules change.' : undefined
  return (
    <span aria-label={sourceLabel ? `${tag.name}: ${sourceLabel}` : undefined} className="tag-pill" title={sourceDescription} style={{ backgroundColor: tag.color, color: getReadableTextColor(tag.color) }}>
      <span className="tag-name">{tag.name}</span>
      {sourceLabel && <small> · {sourceLabel}</small>}
    </span>
  )
}

function ConnectionsPage() {
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
  const [connectionError, setConnectionError] = useState<string>()
  const queryClient = useQueryClient()
  const connectionsQuery = useQuery({
    queryKey: ['provider-connections'],
    queryFn: getProviderConnections,
  })
  const connectMutation = useMutation({
    mutationFn: async () => {
      const session = await startFiskilSession({ name, email, phone })
      const result = await link(session.sessionId)
      if (!result.consentID) {
        throw new Error('Fiskil completed without returning a consent.')
      }

      return completeFiskilSession(session.sessionId, result.consentID)
    },
    onMutate: () => setConnectionError(undefined),
    onSuccess: connection => {
      queryClient.setQueryData<ProviderConnection[]>(['provider-connections'], x => [connection, ...(x ?? []).filter(y => y.id !== connection.id)])
    },
    onError: error => {
      const linkError = error as Partial<LinkError>
      setConnectionError(linkError.code === 'LINK_USER_CANCELLED' ? 'Connection cancelled.' : error instanceof Error ? error.message : 'Unable to connect bank.')
    },
  })
  const disconnectMutation = useMutation({
    mutationFn: disconnectProviderConnection,
    onSuccess: (_result, connectionId) => {
      queryClient.setQueryData<ProviderConnection[]>(['provider-connections'], x => (x ?? []).map(y => y.id === connectionId ? { ...y, status: 'revoked' } : y))
    },
  })
  const canConnect = Boolean(name.trim() && email.trim() && phone.trim()) && !connectMutation.isPending

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <p>Provider access</p>
          <h1>Connections</h1>
        </div>
      </header>
      <section className="panel">
        <div className="panel-header">
          <div>
            <p>Your consent</p>
            <h2>Connect a bank</h2>
          </div>
        </div>
        <p>These details identify you to Fiskil and are used for consent notifications.</p>
        <p>You can also <Link to="/imports">import an OFX export</Link> without connecting a bank.</p>
        <div className="tag-form">
          <input autoComplete="name" onChange={x => setName(x.target.value)} placeholder="Full name" value={name} />
          <input autoComplete="email" onChange={x => setEmail(x.target.value)} placeholder="Email" type="email" value={email} />
          <input autoComplete="tel" onChange={x => setPhone(x.target.value)} placeholder="Phone, e.g. +61412345678" type="tel" value={phone} />
          <button disabled={!canConnect} onClick={() => connectMutation.mutate()} type="button">
            {connectMutation.isPending ? 'Connecting…' : 'Connect bank'}
          </button>
        </div>
        {connectionError && <p role="alert">{connectionError}</p>}
      </section>
      <section className="panel">
        <div className="panel-header">
          <div>
            <p>Household access</p>
            <h2>Connections</h2>
          </div>
        </div>
        {connectionsQuery.isLoading && <p>Loading connections…</p>}
        {!connectionsQuery.isLoading && (connectionsQuery.data?.length ?? 0) === 0 && <p>No banks connected yet.</p>}
        {(connectionsQuery.data ?? []).map(x => (
          <div className="tag-form" key={x.id}>
            <strong>{x.institutionId ? `Institution ${x.institutionId}` : 'Fiskil connection'}</strong>
            <span>{x.status}</span>
            <span>{x.isOwnedByCurrentMember ? 'Connected by you' : 'Connected by a family member'}</span>
            {x.isOwnedByCurrentMember && x.status !== 'revoked' && (
              <button disabled={disconnectMutation.isPending} onClick={() => disconnectMutation.mutate(x.id)} type="button">
                Disconnect
              </button>
            )}
          </div>
        ))}
      </section>
    </section>
  )
}

async function getProviderConnections() {
  return httpClient<ProviderConnection[]>({
    method: 'GET',
    url: '/api/provider-connections',
  })
}

async function startFiskilSession(input: { name: string; email: string; phone: string }) {
  return httpClient<{ sessionId: string; expiresAt: string }>({
    data: input,
    headers: { 'Content-Type': 'application/json' },
    method: 'POST',
    url: '/api/provider-connections/fiskil/session',
  })
}

async function completeFiskilSession(sessionId: string, consentId: string) {
  return httpClient<ProviderConnection>({
    data: { sessionId, consentId },
    headers: { 'Content-Type': 'application/json' },
    method: 'POST',
    url: '/api/provider-connections/fiskil/complete',
  })
}

async function disconnectProviderConnection(connectionId: string) {
  return httpClient<void>({
    method: 'DELETE',
    url: `/api/provider-connections/${connectionId}`,
  })
}

function SettingsPage() {
  const [inviteEmail, setInviteEmail] = useState('')
  const queryClient = useQueryClient()
  const familyQuery = useQuery({ queryKey: ['family'], queryFn: getFamily })
  const inviteMutation = useMutation({
    mutationFn: inviteFamilyMember,
    onSuccess: invitation => {
      queryClient.setQueryData<Family>(['family'], x => x ? { ...x, invitations: [invitation, ...x.invitations] } : x)
      setInviteEmail('')
    },
  })
  const revokeMutation = useMutation({
    mutationFn: revokeFamilyInvitation,
    onSuccess: (_result, invitationId) => queryClient.setQueryData<Family>(['family'], x => x ? { ...x, invitations: x.invitations.filter(y => y.id !== invitationId) } : x),
  })
  const acceptMutation = useMutation({
    mutationFn: acceptDevelopmentInvitation,
    onSuccess: (member, invitationId) => queryClient.setQueryData<Family>(['family'], x => x ? {
      ...x,
      invitations: x.invitations.filter(y => y.id !== invitationId),
      members: [...x.members, member],
    } : x),
  })
  const removeMutation = useMutation({
    mutationFn: removeFamilyMember,
    onSuccess: (_result, memberId) => queryClient.setQueryData<Family>(['family'], x => x ? { ...x, members: x.members.filter(y => y.id !== memberId) } : x),
  })
  const family = familyQuery.data

  const switchDevelopmentMember = (userId: string) => {
    if (!family) {
      return
    }

    const member = family.members.find(x => x.userId === userId)
    if (!member) {
      return
    }

    setDevIdentity({
      userId: member.userId,
      organizationId: family.organizationId,
      role: member.role === 'Owner' ? 'org:admin' : 'org:member',
    })
    window.location.reload()
  }

  return (
    <section className="page family-settings-page">
      <header className="page-header family-page-header">
        <div>
          <p>Household</p>
          <h1>{family?.name ?? 'Family settings'}</h1>
        </div>
      </header>
      {familyQuery.isLoading && <section className="panel"><p>Loading family…</p></section>}
      {familyQuery.isError && <section className="panel"><p role="alert">Unable to load family settings.</p></section>}
      {family?.isDevelopment && (
        <section className="dev-family-banner">
          <div>
            <Shield aria-hidden="true" />
            <div><strong>Development personas</strong><span>Switch users to verify permissions and shared household data.</span></div>
          </div>
          <select onChange={x => switchDevelopmentMember(x.target.value)} value={getDevIdentity().userId}>
            {family.members.map(x => <option key={x.id} value={x.userId}>{x.displayName ?? x.email ?? x.userId} · {x.role}</option>)}
          </select>
        </section>
      )}
      {family?.canManage && (
        <section className="panel family-invite-panel">
          <div className="family-section-heading">
            <div className="family-icon"><UserPlus aria-hidden="true" /></div>
            <div><p>Grow your household</p><h2>Invite a family member</h2><span>Members can connect their own accounts and view shared family finances.</span></div>
          </div>
          <form className="family-invite-form" onSubmit={x => { x.preventDefault(); inviteMutation.mutate(inviteEmail) }}>
            <div><Mail aria-hidden="true" /><input onChange={x => setInviteEmail(x.target.value)} placeholder="family@example.com" type="email" value={inviteEmail} /></div>
            <button disabled={!inviteEmail.trim() || inviteMutation.isPending} type="submit">{inviteMutation.isPending ? 'Sending…' : 'Send invite'}</button>
          </form>
          {inviteMutation.isError && <p className="family-error" role="alert">Unable to send this invitation.</p>}
        </section>
      )}
      {family && (
        <section className="panel family-members-panel">
          <div className="family-section-heading">
            <div className="family-icon"><Users aria-hidden="true" /></div>
            <div><p>People</p><h2>Family members</h2><span>{family.members.length} active {family.members.length === 1 ? 'member' : 'members'}</span></div>
          </div>
          <div className="family-list">
            {family.members.map(x => (
              <div className="family-list-row" key={x.id}>
                <div className="family-avatar">{(x.displayName ?? x.email ?? '?').slice(0, 1).toUpperCase()}</div>
                <div className="family-person"><strong>{x.displayName ?? x.email ?? 'Family member'}{x.isCurrent ? ' (you)' : ''}</strong><span>{x.email ?? x.userId}</span></div>
                <span className={x.role === 'Owner' ? 'family-role is-owner' : 'family-role'}>{x.role}</span>
                {family.canManage && !x.isCurrent && <button className="family-icon-button" aria-label={`Remove ${x.displayName ?? x.email}`} onClick={() => removeMutation.mutate(x.id)} type="button"><Trash2 aria-hidden="true" /></button>}
              </div>
            ))}
          </div>
        </section>
      )}
      {family?.canManage && family.invitations.length > 0 && (
        <section className="panel family-members-panel">
          <div className="family-section-heading"><div className="family-icon"><Mail aria-hidden="true" /></div><div><p>Awaiting response</p><h2>Pending invitations</h2></div></div>
          <div className="family-list">
            {family.invitations.map(x => (
              <div className="family-list-row" key={x.id}>
                <div className="family-avatar is-pending"><Mail aria-hidden="true" /></div>
                <div className="family-person"><strong>{x.email}</strong><span>Invited {new Date(x.createdAt).toLocaleDateString()}</span></div>
                <span className="family-role">Pending</span>
                {family.isDevelopment && <button onClick={() => acceptMutation.mutate(x.id)} type="button">Accept as test member</button>}
                <button className="family-icon-button" aria-label={`Revoke invitation for ${x.email}`} onClick={() => revokeMutation.mutate(x.id)} type="button"><X aria-hidden="true" /></button>
              </div>
            ))}
          </div>
        </section>
      )}
    </section>
  )
}

async function getFamily() {
  return httpClient<Family>({ method: 'GET', url: '/api/family' })
}

async function inviteFamilyMember(email: string) {
  return httpClient<FamilyInvitation>({ data: { email }, headers: { 'Content-Type': 'application/json' }, method: 'POST', url: '/api/family/invitations' })
}

async function revokeFamilyInvitation(invitationId: string) {
  return httpClient<void>({ method: 'DELETE', url: `/api/family/invitations/${invitationId}` })
}

async function acceptDevelopmentInvitation(invitationId: string) {
  return httpClient<FamilyMember>({ method: 'POST', url: `/api/family/invitations/${invitationId}/dev-accept` })
}

async function removeFamilyMember(memberId: string) {
  return httpClient<void>({ method: 'DELETE', url: `/api/family/members/${memberId}` })
}

async function getTransactions(page: number, pageSize: number) {
  return httpClient<TransactionPage>({
    method: 'GET',
    url: `/api/transactions?page=${page}&pageSize=${pageSize}`,
  })
}

async function getTags() {
  return httpClient<TransactionTag[]>({
    method: 'GET',
    url: '/api/tags',
  })
}

async function createTag(input: CreateTagInput) {
  return httpClient<TransactionTag>({
    data: input,
    headers: { 'Content-Type': 'application/json' },
    method: 'POST',
    url: '/api/tags',
  })
}

async function deleteTag(tagId: string) {
  return httpClient<void>({
    method: 'DELETE',
    url: `/api/tags/${tagId}`,
  })
}

async function setTransactionTags(input: SetTransactionTagsInput) {
  return httpClient<TransactionTag[]>({
    data: { tagIds: input.tagIds, manualTagIds: input.manualTagIds },
    headers: { 'Content-Type': 'application/json' },
    method: 'PUT',
    url: `/api/transactions/${input.transactionId}/tags`,
  })
}

async function getMerchantRules() {
  return httpClient<MerchantTagRule[]>({
    method: 'GET',
    url: '/api/merchant-tags',
  })
}

async function createMerchantRule(input: CreateMerchantRuleInput) {
  return httpClient<MerchantTagRule>({
    data: input,
    headers: { 'Content-Type': 'application/json' },
    method: input.ruleId ? 'PUT' : 'POST',
    url: input.ruleId ? `/api/merchant-tags/${input.ruleId}` : '/api/merchant-tags',
  })
}

async function deleteMerchantRule(ruleId: string) {
  return httpClient<void>({
    method: 'DELETE',
    url: `/api/merchant-tags/${ruleId}`,
  })
}

function uniqueTagsById(tags: TransactionTag[]) {
  return tags.filter((x, index) => tags.findIndex(y => y.id === x.id) === index)
}

function getReadableTextColor(backgroundColor: string) {
  const hex = backgroundColor.replace('#', '')
  if (hex.length !== 6) {
    return '#ffffff'
  }

  const red = Number.parseInt(hex.slice(0, 2), 16)
  const green = Number.parseInt(hex.slice(2, 4), 16)
  const blue = Number.parseInt(hex.slice(4, 6), 16)
  const luminance = (red * 0.299 + green * 0.587 + blue * 0.114) / 255
  return luminance > 0.65 ? '#111827' : '#ffffff'
}

const rootRoute = createRootRoute({ component: DashboardShell })
const indexRoute = createRoute({ getParentRoute: () => rootRoute, path: '/', component: DashboardPage })
const connectionsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/connections', component: ConnectionsPage })
const transactionsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/transactions', component: TransactionsPage })
const billingRoute = createRoute({ getParentRoute: () => rootRoute, path: '/billing', component: BillingPage })
const settingsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/settings', component: SettingsPage })
const importsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/imports', component: ImportsPage })
const transfersRoute = createRoute({
  getParentRoute: () => rootRoute, path: '/transfers', component: TransfersPage,
  validateSearch: (search: Record<string, unknown>): { view?: string } => ({
    view: typeof search.view === 'string' && ['suggested', 'confirmed', 'dismissed', 'needs-review'].includes(search.view) ? search.view : undefined,
  }),
})
const routeTree = rootRoute.addChildren([indexRoute, connectionsRoute, transactionsRoute, billingRoute, settingsRoute, importsRoute, transfersRoute])
const router = createRouter({ routeTree })

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
