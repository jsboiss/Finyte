# Finyte Foundation Plan

## Product Goal

Build a customer-facing household finance and bills intelligence app using the same core stack and product direction as the existing Finance application, but designed from the start for paying users, self-service CDR connections, provider consent, operational support, and future scale.

Finyte should help households understand money coming in, money going out, upcoming bills, recurring payments, account balances, utility costs, and provider-linked obligations in one place.

The first version should not try to recreate every Finance feature or every possible Fiskil data domain. It should establish the foundation that makes those features safe to expand later: identity, tenancy, subscription billing, Fiskil integration, customer-owned connections, secure data storage, support tooling, and a clean dashboard shell.

## Existing Reference

The Finance app provides the technical template:

- .NET 10 ASP.NET Core backend.
- Postgres persistence.
- EF Core migrations and query patterns.
- Quartz scheduled jobs.
- React, TypeScript, Vite frontend.
- TanStack Router, Query, and Table.
- Tailwind CSS and shadcn/ui.
- Orval generated API client.
- Banking ingestion, account, transaction, balance, metric, subscription, and budgeting concepts.
- Household cashflow, recurring payment, bill, invoice, and utility-cost concepts.

Finyte should reuse the proven architectural shape, but it should not copy the old assumptions around personal use, external friend API access, or Redbark-specific banking flows. The app should be shaped around household financial visibility, not only bank account tracking.

## Important Product Shift

Finance was built for trusted personal use and a narrow external API surface.

Finyte needs to be built as a real service:

- Users create and manage their own accounts.
- Users control their own banking, energy, and future CDR data-domain connections and consent.
- Users pay through a subscription or billing portal.
- Banking, income, identity, energy, bill, and invoice data must be isolated per customer and tenant.
- Administrative access must be explicit, audited, and limited.
- Provider integration should be Fiskil-first rather than Redbark-first.
- Compliance, privacy, consent withdrawal, and data deletion must be product requirements, not later cleanup.

## Product Scope Shift

The Fiskil API surface supports more than bank accounts and transactions. Banking data can include accounts, balances, transactions, payees, direct debits, and scheduled payments. Energy data can include accounts, usage, billing history, invoices, concessions, service points, plans, distributed energy resources, and scheduled payments. Fiskil also exposes identity data and income insights derived from banking data.

That means Finyte should be designed as a household intelligence product with multiple consented data domains:

- Banking: balances, transactions, payees, direct debits, scheduled payments, recurring spending, and cashflow.
- Income: salary and recurring inflow detection, payday cadence, and income stability.
- Energy: utility accounts, bills, invoices, due dates, usage, concessions, service points, plans, and solar or battery data where available.
- Identity: verified identity and account ownership checks where the product has a clear need and compliant retention rules.

Do not model Fiskil as only a bank connection provider. Model it as a provider integration that can deliver multiple consented data domains for one household.

## Regulatory And Business Notes

Fiskil appears to provide CDR-compliant banking and energy API infrastructure and may support a CDR Representative style model. Before production customer onboarding, confirm directly with Fiskil what your business will be under their arrangement, what policies you must publish, what consent screens and disclosures are required for each data domain, and what data retention/deletion obligations apply.

Do not treat this plan as legal or compliance advice. Before accepting paying customers, get explicit confirmation on:

- Whether your partnership can operate under Fiskil as a CDR Representative.
- Required privacy policy, CDR policy, terms of service, and consent wording.
- Required business email, domain, ABN, support contact, and incident contact details.
- Whether CDR data must remain hosted in Australia.
- Whether any outsourced infrastructure or logging provider needs disclosure.
- Rules around storing raw banking data, energy data, identity data, income insights, derived insights, backups, and deleted user data.

## Recommended Scope Strategy

Build the foundation in small releases. Each release should leave the app usable and safer than before.

Avoid starting with the full Finance feature set. The hard part is not charts or tables; the hard part is making customer data, consent, billing, and operations boring and reliable.

## Phase 0: Business And Provider Readiness

Goal: be ready to build against the real provider and onboard test users without rework.

Deliverables:

- Choose and register the domain.
- Set up business email.
- Start Fiskil onboarding.
- Confirm Fiskil product, pricing, environments, auth model, webhooks, consent flows, and data retention requirements.
- Decide the public product name, support email, and initial pricing model.
- Draft required legal pages:
  - Privacy Policy.
  - Terms of Service.
  - CDR or data sharing policy if required.
  - Support and contact page.
  - Data deletion and account closure process.
- Decide whether v1 supports individuals only, couples/families, businesses, or multiple household members.

Engineering outputs:

- `PLAN_Finyte.md`.
- Initial architecture decision records for Fiskil, auth, billing, and hosting assumptions.
- Environment variable inventory for local, staging, and production.

Do not build production banking or energy ingestion until Fiskil confirms the exact integration, consent, permission, and retention model.

## Phase 1: Greenfield Application Skeleton

Goal: create the new app with the same stack and clean boundaries.

Backend:

- Create `.slnx` solution targeting .NET 10.
- Projects:
  - `src/Finyte.Api`
  - `src/Finyte.Core`
  - `src/Finyte.Data`
  - `src/Finyte.Web`
  - `tests/Finyte.Tests`
  - `tests/Finyte.IntegrationTests`
- Add ASP.NET Core API host.
- Add Postgres and EF Core.
- Add migrations.
- Add Quartz.
- Add OpenAPI generation for Orval.
- Add health checks.
- Add structured logging.

Frontend:

- React + TypeScript + Vite.
- TanStack Router.
- TanStack Query.
- TanStack Table.
- Tailwind CSS.
- shadcn/ui.
- Orval client generation.
- Dashboard shell with empty authenticated routes.

Local development:

- Docker Compose for Postgres.
- Backend dev server.
- Vite frontend dev server.
- Vite proxy to backend.
- Seed script or development fixtures.

Acceptance:

- App starts locally.
- Frontend can call backend through generated client.
- EF migrations create the database.
- Health check verifies database connectivity.

## Phase 2: Identity, Tenancy, And Account Model

Goal: users can sign up, log in, and own isolated data.

Core concepts:

- User.
- Tenant or workspace.
- Tenant membership.
- Role.
- Invitation.
- Session.
- Audit event.

Recommended v1 model:

- Every signup creates one tenant.
- A tenant can eventually represent a household, couple, family, or small business.
- Keep multi-member tenants possible, even if v1 only exposes single-user accounts.
- Tenant id is resolved server-side from the authenticated user.
- Never trust tenant id from normal request bodies.

Authentication options:

- Use ASP.NET Core Identity if you want maximum .NET control.
- Use a managed provider if you want less security surface and faster launch.

Recommended decision:

- For a solo-developed financial app, strongly consider managed auth unless you have a strong reason to own password storage, MFA, reset flows, breach handling, and account recovery yourself.
- If using ASP.NET Core Identity, require email verification and design MFA support early.

Backend deliverables:

- Register.
- Login.
- Logout.
- Email verification.
- Password reset.
- Current user endpoint.
- Tenant resolution service.
- Authorization policies.
- Audit logging for identity and tenant-sensitive events.

Frontend deliverables:

- Login page.
- Register page.
- Verify email page.
- Reset password page.
- Authenticated dashboard layout.
- Account settings page.

Acceptance:

- A new user can register and log in.
- A logged-in user can only access their own tenant.
- Integration tests prove tenant isolation.

## Phase 3: Billing And Subscription Foundation

Goal: users can become paying customers before expensive banking infrastructure is exposed broadly.

Recommended provider:

- Stripe Billing is the default choice unless there is a strong local requirement for another provider.

Data model:

- Subscription plan.
- Customer billing profile.
- Subscription status.
- Billing event.
- Entitlement.

Backend deliverables:

- Create checkout session.
- Billing portal session.
- Webhook endpoint.
- Idempotent webhook event storage.
- Subscription status sync.
- Entitlement checks.

Frontend deliverables:

- Pricing or plan selection page.
- Billing settings page.
- Subscription status display.
- Manage billing button.

Product rules:

- Decide whether provider connections are allowed before payment.
- Recommended v1: allow account creation without payment, but require an active trial or subscription before connecting banking or energy providers.

Acceptance:

- Stripe checkout updates local subscription state.
- Billing portal can cancel or update payment method.
- Webhook replay does not duplicate records.
- App can enforce paid/trial access.

## Phase 4: Fiskil Integration Spike

Goal: prove the direct Fiskil integration in isolation before building the full household finance product around it.

Scope:

- Fiskil sandbox credentials.
- OAuth or consent initiation flow.
- Callback handling.
- Provider customer mapping.
- Connection status retrieval.
- Permission and data-domain mapping.
- Accounts retrieval.
- Balances retrieval.
- Transactions retrieval.
- Direct debits and scheduled payments retrieval.
- Income retrieval after banking transaction sync.
- Energy account, billing, invoice, and payment schedule retrieval if enabled in sandbox.
- Webhook verification.
- Consent withdrawal handling.

Backend deliverables:

- Typed Fiskil client.
- Provider abstraction shaped around Finyte concepts, not Fiskil names.
- Data-domain abstraction for banking, income, identity, and energy datasets.
- Secure storage for provider tokens or provider references, depending on Fiskil model.
- Webhook endpoint with raw body verification if Fiskil signs payloads.
- Durable provider event table.
- Minimal import run table.

Data model:

- Provider connection.
- Connection consent.
- Connection data domain.
- Bank account.
- Balance snapshot.
- Transaction.
- Direct debit.
- Scheduled payment.
- Income source.
- Energy account.
- Energy bill or billing transaction.
- Energy invoice.
- Energy usage read.
- Provider event.
- Import run.

Important design rule:

- Keep Fiskil-specific DTOs at the infrastructure boundary.
- Store normalized Finyte entities in domain tables.
- Keep consent and permission state explicit per connection and data domain.
- Keep selected raw provider JSON only where needed for audit/debugging and only if permitted.

Acceptance:

- A test user can start a Fiskil banking connection.
- Callback or consent completion is handled.
- Accounts and balances import for one connection.
- Transactions import idempotently.
- Scheduled payments or direct debits import where available.
- Energy billing or invoice data imports if enabled in the sandbox account.
- Disconnect or consent withdrawal updates local state.

## Phase 5: Customer Provider Connection Management

Goal: users can manage their own connections without admin help.

Frontend deliverables:

- Connected providers page.
- Add bank connection button.
- Add energy connection button if enabled for the tenant.
- Connection status display.
- Connected data domains and granted permissions display.
- Last sync time.
- Reconnect or refresh action where supported.
- Disconnect action.
- Consent expiry or renewal state if applicable.

Backend deliverables:

- List user tenant connections.
- Start connection.
- Handle connection callback.
- Refresh connection data.
- Disconnect connection.
- Mark consent withdrawn or expired.
- Schedule sync jobs.

Operational behavior:

- Initial import should be queued.
- UI should show import status.
- Failed imports should be visible and retryable.
- Provider errors should be normalized into user-safe messages.

Acceptance:

- User can add, view, refresh, and disconnect a banking provider.
- User can add, view, refresh, and disconnect an energy provider when enabled.
- Import progress is visible.
- Errors do not expose sensitive provider details.

## Phase 6: Household Finance And Bills Foundation UI

Goal: deliver the minimum useful product after connection.

Views:

- Dashboard overview.
- Account list.
- Account detail.
- Transaction table.
- Balance summary.
- Upcoming payments and direct debits.
- Bills and invoices.
- Energy account summary if enabled.
- Simple income and cashflow summary.
- Recent sync/import status.

Features:

- Account filtering.
- Date filtering.
- Transaction search.
- Bill and invoice filtering.
- Upcoming payment timeline.
- Basic cashflow projection from income, scheduled payments, direct debits, and known bill due dates.
- Sorting.
- Pagination.
- Loading, empty, and error states.

Keep this phase deliberately simple. Do not migrate all Finance analytics yet.

Acceptance:

- A paying or trial user can connect a bank and see accounts, balances, posted transactions, and known upcoming payments.
- If energy is enabled, the user can see energy accounts, recent bills or invoices, due dates, and billing history.
- The app is useful even without budgets, subscription detection, plan comparison, solar analysis, or advanced metrics.

## Phase 7: Admin And Support Console

Goal: operate the product safely when real users exist.

Admin features:

- Search users.
- View tenant summary.
- View subscription status.
- View connection health.
- View import runs.
- Retry failed imports.
- Disable account.
- Trigger account deletion workflow.

Security requirements:

- Separate admin role.
- Admin actions audited.
- Avoid showing full transaction details unless explicitly needed.
- No impersonation in v1 unless carefully designed and audited.

Acceptance:

- You can support a user with a broken connection.
- You can diagnose import failures.
- Admin access is limited and logged.

## Phase 8: Production Readiness And Hosting

Goal: deploy safely for private beta.

Hosting requirements:

- Australian region if required by Fiskil/CDR obligations.
- Managed Postgres.
- Automated backups.
- Secret management.
- TLS.
- Custom domain.
- Email sending.
- Background worker support.
- Logs and metrics.
- Error tracking.
- Deployment rollback path.

Candidates to evaluate later:

- Azure App Service or Azure Container Apps with Azure Database for PostgreSQL.
- AWS ECS/Fargate or App Runner with RDS Postgres.
- Google Cloud Run with Cloud SQL Postgres.
- Fly.io with Australian region availability if requirements fit.
- Railway only if scale, compliance, backup, and region requirements are acceptable.

Private beta checklist:

- Staging environment.
- Production environment.
- Database migrations in deployment flow.
- Backup restore test.
- Error alerting.
- Uptime checks.
- Basic rate limiting.
- Security headers.
- Webhook replay protection.
- Incident contact process.

Acceptance:

- Production can serve the app on the real domain.
- A staging environment can test Fiskil and Stripe without affecting production.
- Backups are proven restorable.

## Phase 9: Migrate Finance Features

Goal: bring over the value from Finance once the platform is trustworthy.

Recommended order:

1. Transaction categorisation.
2. Merchant/payee cleanup.
3. Subscription detection.
4. Monthly cashflow metrics.
5. Spending trends.
6. Budgeting.
7. Net worth or balance history.
8. Alerts and notifications.
9. Exports.
10. External API access if still part of the product.

Migration approach:

- Move one domain feature at a time.
- Keep each feature tenant-safe from the first commit.
- Add integration tests around tenant isolation.
- Avoid copying Redbark-specific assumptions into Finyte.
- Reuse UI patterns, not necessarily files, where Finance was personal-use oriented.

## Phase 10: External API Access

Goal: expose customer or partner API access only after the internal product model is stable.

Do not start here. External API access adds security, support, rate limiting, documentation, versioning, key rotation, and billing complexity.

Foundation:

- Tenant-scoped API clients.
- Hashed API keys.
- Key rotation.
- Scopes.
- Rate limits.
- Usage logging.
- API versioning.
- OpenAPI document.
- Developer documentation.

Recommended v1 external endpoints:

- `/api/v1/accounts`
- `/api/v1/transactions`
- `/api/v1/balances`
- `/api/v1/connections/status`

Acceptance:

- API keys cannot access other tenants.
- Keys can be revoked.
- Usage can be traced.
- Rate limits protect the product.

## Cross-Cutting Requirements

Security:

- MFA-ready auth.
- Email verification.
- Strong password policy if self-hosting auth.
- Tenant isolation tests.
- No plaintext API keys.
- No bank credentials stored.
- No utility provider credentials stored.
- Secrets only in secret manager or local user secrets.
- Secure cookies.
- CSRF protection where needed.
- Rate limiting on auth and sensitive endpoints.

Privacy and data:

- Data minimisation.
- Clear retention rules.
- Account deletion workflow.
- Consent withdrawal workflow.
- Audit logs.
- Backups with retention policy.
- Avoid logging CDR data, tokens, account numbers, invoice details, service point identifiers, identity data, or full transaction descriptions unless deliberately allowed.

Reliability:

- Idempotent imports.
- Durable webhook event storage.
- Retryable background jobs.
- Import run visibility.
- Provider error normalization.
- Scheduled reconciliation.
- Manual support retry.
- Dataset-level sync status so one failed domain does not hide successful imports from another.

Testing:

- Unit tests for domain logic.
- Integration tests with Postgres Testcontainers.
- Auth and tenant isolation tests.
- Webhook signature tests.
- Import idempotency tests.
- Billing webhook tests.
- Minimal Playwright coverage for signup, billing gate, connection page, transaction table, and bills view.

Observability:

- Structured logs.
- Correlation ids.
- Request logging.
- Background job logging.
- Error tracking.
- Metrics for imports, webhook failures, sync latency, and billing webhook failures.
- Metrics by provider, data domain, and sync dataset.

## Suggested Initial Data Model

Identity and tenancy:

- `users`
- `tenants`
- `tenant_memberships`
- `invitations`
- `audit_events`

Billing:

- `billing_customers`
- `subscriptions`
- `billing_events`
- `entitlements`

Provider integration:

- `provider_connections`
- `connection_consents`
- `connection_data_domains`
- `provider_events`
- `import_runs`

Banking:

- `bank_accounts`
- `balance_snapshots`
- `transactions`
- `payees`
- `direct_debits`
- `scheduled_payments`
- `income_sources`

Energy and bills:

- `energy_accounts`
- `energy_service_points`
- `energy_billing_transactions`
- `energy_invoices`
- `energy_usage_reads`
- `energy_concessions`
- `energy_payment_schedules`

Household intelligence:

- `bill_obligations`
- `recurring_obligations`
- `cashflow_forecasts`

Operations:

- `background_jobs` if Quartz metadata is not enough.
- `support_notes` only if needed.

External API later:

- `api_clients`
- `api_key_hashes`
- `api_usage_events`

## First Milestone Recommendation

The first real milestone should be:

"A paying or trial user can sign up, verify email, subscribe or enter trial, connect one bank through Fiskil sandbox, optionally connect one energy provider if enabled, and see accounts, balances, posted transactions, upcoming payments, recent bills or invoices, and a simple household cashflow view in their own dashboard."

That milestone proves the actual business:

- User acquisition path.
- Identity.
- Billing.
- Provider connection and consent.
- Banking and bills data ingestion.
- Tenant isolation.
- Customer-facing UI.
- Provider integration.
- Operational visibility.

Everything after that is product depth.

## Near-Term Build Order

1. Confirm Fiskil onboarding requirements and legal/compliance obligations.
2. Create the Finyte solution and frontend shell.
3. Add identity and tenant model.
4. Add Stripe billing foundation.
5. Build Fiskil sandbox spike for banking first, with provider/data-domain abstractions ready for energy.
6. Add provider connection management.
7. Add account, balance, transaction, upcoming payment, and bills UI.
8. Add admin/support console.
9. Deploy staging.
10. Run private beta with one or two trusted users.

## What Not To Do Yet

- Do not migrate every Finance metric immediately.
- Do not build every Fiskil data domain immediately.
- Do not build external API access before the customer product is stable.
- Do not design for 1000 users before one real customer can connect providers reliably.
- Do not lock into a hosting provider before Fiskil confirms region, compliance, and data residency requirements.
- Do not store more provider raw data than you need.
- Do not make admin access informal.

## Success Criteria For Foundation

The foundation is complete when:

- Users can self-register and manage their account.
- Tenants are isolated by design and by tests.
- Billing state controls product access.
- Fiskil connections can be created, synced, and disconnected.
- Provider consent and permission state is tracked per data domain.
- Banking data imports idempotently.
- Energy bill or invoice data can be imported when enabled.
- Users can view their own accounts, balances, transactions, upcoming payments, bills, and simple cashflow view.
- You can diagnose failed imports without database spelunking.
- Staging and production environments exist.
- Legal, privacy, and support surfaces are ready for private beta.
