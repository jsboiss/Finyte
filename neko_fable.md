# neko_fable.md — Finyte Documentation Rules & Recommendations

A distilled rulebook built from **all** the local skill documentation in `.codex/skills/`. Read this before touching auth, provider integration, or webhook code. Where this document and the raw docs disagree, the raw docs win — and the snapshot dates matter (Fiskil snapshot generated 2026-05-21; re-run `scripts/refresh_docs.ps1` in the `fiskil-api` skill before finalising anything security-sensitive).

**Sources covered:**

| Area | Docs |
| --- | --- |
| Clerk | `.codex/skills/clerk-setup`, `clerk-react-patterns`, `clerk-custom-ui` (core-2 + core-3), `clerk-orgs`, `clerk-backend-api`, `clerk-cli`, `clerk-testing`, `clerk-webhooks` — every SKILL.md and references file |
| Fiskil | `.codex/skills/fiskil-api` — SKILL.md, manifest, all guides (getting started, core concepts, data domains, link widget, account access, resources), API reference (v1-0-0 and v2-0-0 endpoint pages, permissions, pagination, errors), and changelog |

**Project context:** Finyte is a household finance app — .NET 10 ASP.NET Core API + Postgres backend, React 19 / TypeScript / Vite / TanStack mobile-first PWA frontend, Clerk for auth, Fiskil for consented CDR banking/energy/identity/income data. See `PLAN_Finyte.md` (foundation) and `PLAN_Mobile_UI.md` (frontend).

**Checked against real code as of `main@11cbc58`** ("0000 working branch #4" — tenancy, Stripe billing, dashboard projections, and a first Fiskil sync implementation landed after this document was first written). **Part 4** below records where that implementation already diverges from the rules in Parts 2–3, including one live security gap. Re-check Part 4 whenever `Finyte.Data.ProviderSync` or `Finyte.Api.Endpoints.FiskilWebhookEndpoints` changes.

---

## Golden Rules (the ten that matter most)

1. **Secrets are server-only.** `CLERK_SECRET_KEY`, `CLERK_WEBHOOK_SIGNING_SECRET`, Fiskil `client_id`/`client_secret`, and the Fiskil webhook signing secret never appear in frontend code, the Vite bundle, logs, or committed files. Only Clerk publishable keys (`pk_*`, via `VITE_CLERK_PUBLISHABLE_KEY`) are allowed client-side.
2. **The client never talks to Fiskil.** All calls to `api.fiskil.com` go through the .NET backend; the PWA only receives a `session_id` to hand to the Link SDK.
3. **Verify every webhook signature on the raw body before parsing** — Svix headers for Clerk, `X-Fiskil-Signature` (HMAC-SHA256, base64-decoded secret, constant-time compare) for Fiskil. Unverified endpoints accept spoofed events.
4. **Webhook handlers ack fast, process async, and dedupe** — `svix-id` for Clerk, `message_id` for Fiskil. Fiskil retries only 5 times (1–10 min backoff) then **discards the event forever**, so keep a reconciliation poll as backstop.
5. **Client-side auth state is UX only.** `isSignedIn`, `<Show>`, `has?.()` hide pixels; the .NET backend re-derives user and tenant from the validated Clerk JWT on every request. Tenant/org/household ID comes from the token, never from the request body or URL alone.
6. **Pin the Fiskil API version in code**: send `X-Fiskil-Version: v2` on every request. Never rely on the Console pin (upgrading it is one-way), never send full semvers, never mix v1 and v2 DTO shapes.
7. **Parse provider responses defensively.** Both Clerk and Fiskil ship additive changes without notice: ignore unknown JSON fields, treat enums as strings with an unknown fallback, and map Fiskil monetary/rate strings to `decimal`, never `double`.
8. **Clerk metadata updates REPLACE, not merge.** Always read → spread → write. Same class of gotcha: `clerk config put` is a full replacement — `--dry-run` every mutation.
9. **Consent state is driven by server-side events, not client callbacks.** The Link SDK resolving successfully is a UX hint; `consent.received` / `consent.updated` / `consent.revoked` webhooks (reconciled against `GET /v1/consent`) are the source of truth.
10. **CDR obligations are product requirements**: data minimisation, documented retention, deletion/de-identification pipelines on consent revocation and account closure, and data-sovereignty-compliant storage. Log `end_user_id`, `consent_id`, `session_id`, `error_id`, and webhook `message_id` on every provider interaction — and never log tokens, account numbers, or payload data.

---

# Part 1 — Clerk Rules & Recommendations

## 1.1 clerk-setup

**Summary:** Adding Clerk to a project — agent-first provisioning via the `clerk` CLI (`clerk init`, `clerk link`, `clerk env pull`), framework detection and quickstart URLs, keyless vs manual key acquisition, secret-key rotation via the Platform API, migrations from other auth providers, current package naming, `ClerkProvider` placement, and a common-pitfalls table.

**Rules:**

- **Never expose `CLERK_SECRET_KEY` in client code.** Only publishable keys (`pk_test_`/`pk_live_`, framework-prefixed env vars like `VITE_CLERK_PUBLISHABLE_KEY`) are safe on the client.
- Use the current package name `@clerk/react` — never the Core 2 name `@clerk/clerk-react`.
- Vite apps must use the `VITE_CLERK_PUBLISHABLE_KEY` env var name and read it via `import.meta.env` — no other prefix works.
- Current SDK requires **Node.js 20.9.0 or higher** (Core 2 allowed 18.17.0).
- Server code imports from server subpaths (`@clerk/nextjs/server`-style); client code from the root package — never mix import paths.
- Prefer `clerk init --framework <react|...> -y` / `clerk link` / `clerk env pull` over manual Dashboard clicks; `env pull` auto-detects framework env var names and target files (`.env.development.local` > `.env.local` > `.env`).
- In agent/scripted mode, `clerk link` without `--app` only works when a `CLERK_PUBLISHABLE_KEY` already exists in `.env`; otherwise run `clerk apps list --json` and ask which `app_id` to link — do not guess.
- Rotate secret keys via `clerk api --platform POST /v1/platform/applications/<app_id>/rotate_secret_keys` with `delay_old_secrets_expiration_hours` so old keys stay valid during rollout.
- Run `clerk doctor` first when debugging setup — it checks env vars, integration, and SDK install status.
- Themes come from `@clerk/ui` in the current SDK (Core 2: `@clerk/themes`). If a project has shadcn/ui (`components.json`), always apply the `shadcn` theme.
- Keyless mode can auto-generate dev keys on first SDK initialization; otherwise get keys from `dashboard.clerk.com/~/api-keys`.
- When migrating from another provider: store legacy user IDs as `external_id` in Clerk; Clerk upgrades password hashes to Bcrypt transparently; existing sessions terminate on switch.

**Finyte recommendations:**

- Frontend uses `@clerk/react` with `VITE_CLERK_PUBLISHABLE_KEY` in `.env` (Vite convention). Keep `CLERK_SECRET_KEY` only in the .NET backend's configuration/secrets — it must never appear in the Vite bundle or repo.
- Use `clerk env pull` for local key refresh and `clerk env pull --instance prod` for production keys; `.env.local`/`.env` files must be gitignored.
- The canonical setup reference for Finyte's frontend is `https://clerk.com/docs/react/getting-started/quickstart`.

## 1.2 clerk-react-patterns

**Summary:** React SPA auth patterns for `@clerk/react` in Vite apps — `ClerkProvider` setup, the `useAuth()` / `useUser()` / `useClerk()` hooks and their `isLoaded` guard semantics, `getToken()` for authenticated API calls, protected-route guards, org-gated routes, custom sign-in/sign-up flows with `useSignIn`/`useSignUp`, and router integration.

**Rules:**

- **`@clerk/react` is client-only — there is no server-side `auth()`.** All auth state comes from hooks; the server must validate the JWT itself.
- **Always guard on `isLoaded` before trusting `isSignedIn`** — `isSignedIn` is `undefined` until Clerk initializes; rendering before that gives wrong results.
- **`getToken()` returns `null` when unauthenticated — always null-check** before attaching `Authorization: Bearer ${token}` headers.
- `ClerkProvider` must be at the root, wrapping the router, and must be an ancestor of every Clerk hook/component.
- Pass `publishableKey` explicitly to `ClerkProvider`; a missing key renders sign-in components blank.
- Protected routes: a guard component that checks `isLoaded` → loading UI, `!isSignedIn` → redirect to `/sign-in`, then renders the outlet. Org-gated routes additionally redirect when `orgId` is null.
- Routes hosting the prebuilt `<SignIn />`/`<SignUp />` components must be registered with a trailing wildcard (e.g. `/sign-in/*`) so Clerk's multi-step flows work.
- In custom flows, always check `result.status === 'complete'` before calling `setActive({ session: result.createdSessionId })`; handle `'needs_second_factor'`, `'needs_new_password'`, and `'missing_requirements'` explicitly.
- Pass a callback to `signOut(() => navigate('/'))` to navigate only after sign-out completes.
- Redirect configuration: `afterSignInUrl` / `afterSignUpUrl` props on `ClerkProvider`, or env vars `VITE_CLERK_SIGN_IN_URL`, `VITE_CLERK_SIGN_UP_URL`, `VITE_CLERK_SIGN_IN_FALLBACK_REDIRECT_URL`, `VITE_CLERK_SIGN_UP_FALLBACK_REDIRECT_URL`.
- `useClerk()` is the surface for programmatic actions: `signOut`, `openSignIn`, `openUserProfile`, `openOrganizationProfile`.

**Finyte recommendations:**

- The docs' route-guard examples use React Router; translate the same pattern to TanStack Router: gate in a layout route component (TanStack Router's `beforeLoad` cannot use hooks, so gate in render or pass Clerk state into router context). The invariant is identical: never redirect or render protected content while `isLoaded === false`.
- Wrap `getToken()` in one shared fetch helper (the docs' `useAuthFetch` pattern) used by TanStack Query's `queryFn`/`mutationFn` so every call to the .NET API carries `Authorization: Bearer <session JWT>`. Call `getToken()` fresh per request — Clerk rotates short-lived session tokens; do not cache the token in Query state. (This is what `app/auth/AuthTokenProvider.tsx` + `app/api/httpClient.ts` implement — keep it the single path.)
- The SPA only *fetches* tokens; validation happens entirely in the .NET backend.
- Prefer guard routes for whole route trees (dashboard, accounts, bills) and `<Show>`/conditional rendering only for element-level visibility.

## 1.3 clerk-custom-ui

**Summary:** (1) Custom auth flows via `useSignIn()`/`useSignUp()` — with **completely different APIs** between Core 2 (`signIn.create` / `prepareFirstFactor` / `attemptFirstFactor` / `setActive`) and current SDK v7+/Core 3 (`signIn.password()`, `signIn.emailCode.sendCode()`, `signIn.mfa.verifyTOTP()`, `signIn.finalize()`, structured `errors`, `fetchStatus`), plus the Core 3 `<Show>` component; (2) appearance customization of prebuilt components via the `appearance` prop (`variables`, `options`, themes from `@clerk/ui`).

**Rules:**

- **Check the SDK version first — Core 2 and current have completely different `useSignIn`/`useSignUp` shapes.** Core 2 returns `{ signIn, isLoaded, setActive }`; Core 3 returns `{ signIn, errors, fetchStatus }`.
- **`<Show>` only visually hides content — it remains in browser source. It is not a security boundary.** Always verify authentication server-side for sensitive data.
- In Core 3, finish every successful flow with `signIn.finalize({ navigate })` / `signUp.finalize({ navigate })`; inside `navigate`, `decorateUrl(path)` may return an **absolute** URL (Safari ITP support) — if it starts with `http`, use `window.location.href`, otherwise the router.
- Check `session.currentTask` in `finalize()` before redirecting — pending session tasks (e.g. `choose-organization`) must be handled.
- Custom sign-up pages must include `<div id="clerk-captcha" />` — bot sign-up protection is enabled by default.
- Core 3 MFA: a second factor is required when `signIn.status` is `'needs_second_factor'` **or** `'needs_client_trust'` (new device without MFA); check `signIn.supportedSecondFactors` for available methods.
- Handle errors with `isClerkAPIResponseError()` (Core 2) or the Core 3 structured `errors.fields.*` / `errors.global` / `errors.raw` object; all Core 3 methods return `Promise<{ error: ClerkError | null }>`.
- Appearance: use `colorPrimary` (not `primaryColor`); `logoImageUrl` and `socialButtonsVariant` go inside `options: {}` (Core 2 called it `layout: {}`); use the `appearance` prop, not direct CSS, unless using bring-your-own-css.
- Themes install from `@clerk/ui` (`import { dark } from '@clerk/ui/themes'`); theme stacking via array where the last theme wins; `colorRing` and `colorModalBackdrop` now render at full opacity — use explicit `rgba()` for transparency.
- If `components.json` (shadcn/ui) exists, apply the `shadcn` theme first and import `@clerk/ui/themes/shadcn.css`.
- `treatPendingAsSignedOut` defaults to `true` on `<Show>` — pending-task sessions read as signed-out unless you opt out.
- Core 3 `<Show>` replaces `<SignedIn>`/`<SignedOut>`/`<Protect>`: `when="signed-in"`, `when={{ role: 'org:admin' }}`, `when={{ permission: '...' }}`, `when={(has) => ...}`, with `fallback`.

**Finyte recommendations:**

- Pin which Core generation Finyte's `@clerk/react` is on before writing any custom flow, and record it here — the two hook APIs are not interchangeable.
- For the mobile-first PWA, prebuilt components + `appearance` `variables` (e.g. `colorPrimary`, `borderRadius`) styled to match Tailwind v4 tokens is the low-risk default; only build custom flows if the prebuilt UI can't fit the mobile UX.
- The Safari ITP handling in `finalize()`'s `decorateUrl` is directly relevant to an iOS-installed PWA — always implement the absolute-URL branch, wiring the relative branch to TanStack Router's `router.navigate`.
- Treat `<Show>`/role checks purely as UX; every household-finance data access is enforced by the .NET backend.

## 1.4 clerk-orgs

**Summary:** Clerk Organizations for multi-tenant apps — enabling Organizations and choosing Membership mode (`Membership required`, default since 2025-08-22, disables personal accounts; vs `Membership optional` for B2C+B2B coexistence), org CRUD/memberships/invitations, RBAC with `has({ role })` / `has({ permission })`, the System Permissions catalog, custom roles and Role Sets, `<OrganizationSwitcher />`, the `choose-organization` session task, Enterprise SSO, URL-slug safety invariants, and metadata-overwrite gotchas.

**Rules:**

- **Organizations must be enabled first** (Dashboard → Organizations settings, or `clerk enable orgs`) — no org API, hook, or component works otherwise. Pick Membership mode deliberately.
- **Never invent permission slugs.** The only built-in permissions are `org:sys_profile:{manage,delete}`, `org:sys_memberships:{read,manage}`, `org:sys_domains:{read,manage}`, `org:sys_billing:{read,manage}`. Names like `org:create` or `org:manage_members` do not exist. Custom permissions follow `org:<resource>:<action>`; the `org:` prefix is mandatory; slugs are case-sensitive.
- **Always validate `orgSlug === params.slug` on every org-scoped surface** — middleware route matching does not verify the URL slug against the active org; users can hit `/orgs/other-org/...` with a different active org.
- **Bind `orgId` from the session at the database layer. Never trust a client-supplied org identifier** — this prevents cross-org writes.
- Roles must be prefixed: `"org:admin"` / `"org:member"` (or custom `org:<role>` slugs).
- **Metadata updates REPLACE, not merge** — `updateOrganization({ publicMetadata })` (and user metadata) overwrites everything. Read first, spread, then write.
- On the client, check `isLoaded` before trusting `has` — it can be `undefined` on first render; use `has?.()`.
- Role changes require a session refresh — stale tokens make `has()` return old values; `await clerk.session?.reload()` or re-sign-in.
- `createOrganizationInvitation` requires `inviterUserId` in any human-initiated flow; callers need `org:sys_memberships:manage`; revoke by `invitationId` (not email); expired invitations must be recreated, not re-sent.
- Invitation rate limits: single create 250/hr, `createOrganizationInvitationBulk` 50/hr per instance.
- The Creator Role must carry at minimum `org:sys_memberships:manage`, `org:sys_memberships:read`, `org:sys_profile:delete`.
- Enterprise SSO: strategy is `'enterprise_sso'` in Core 3 (Core 2: `'saml'`); `provider` lives on `enterpriseAccounts[i].enterpriseConnection?.provider`; Enterprise SSO and Verified Domains are mutually exclusive per domain; SSO auto-join is JIT Provisioning.
- When Clerk Billing is enabled, `has({ permission })` returns `false` if the permission's Feature isn't in the org's active Plan, regardless of role.
- Cap seats with `maxAllowedMemberships` at org creation or via `updateOrganization`.
- Max 10 custom roles per instance; roles are surfaced to orgs via Role Sets (one Role Set per org).
- `redirect()` throws — don't put code after it.

**Finyte recommendations:**

- Organizations are the natural model for a **household** (matching `PLAN_Finyte.md`'s tenant concept): one org per household, `org:admin` for the household owner, `org:member` for others. Choose **`Membership optional`** if a user should be able to use Finyte solo before/without joining a household; `Membership required` forces the `choose-organization` task after sign-in.
- The core security invariant: every .NET endpoint that reads/writes household finance data derives the household/org ID from the validated Clerk JWT's org claim — never from the request body or URL alone; if a URL carries an org slug/id, verify it matches the token's active org.
- Use `<OrganizationSwitcher>` with `afterSelectOrganizationUrl` for household switching; sync membership via `organizationMembership.*` webhooks.
- Manage invites either with the zero-code `<OrganizationProfile />` members tab or backend-driven `createOrganizationInvitation` with `inviterUserId` and a permission check.

## 1.5 clerk-backend-api

**Summary:** The Clerk Backend REST API (`https://api.clerk.com/v1`, `Authorization: Bearer $CLERK_SECRET_KEY`) — user/org/invitation/metadata operations, the three metadata types, mandatory safety checks before writes/deletes, rate limits, and OpenAPI-driven endpoint discovery.

**Rules:**

- Every Backend API request authenticates with `Authorization: Bearer $CLERK_SECRET_KEY` — server-side only.
- **Metadata trust boundaries:** `public_metadata` — client-readable, **server-only writable** (plan tier, roles, feature flags); `private_metadata` — **server-only** read and write (Stripe IDs, compliance flags); `unsafe_metadata` — client-writable, never put anything sensitive or authorization-relevant in it.
- **Metadata updates REPLACE, not merge.** `updateUser({ publicMetadata: { newField } })` deletes all other public metadata fields. Read, spread, write.
- REST API uses `snake_case` (`public_metadata`); SDKs use `camelCase` (`publicMetadata`).
- **`DELETE /v1/users/{user_id}` is irreversible** — it destroys the user record, all sessions, all memberships, and associated data. Always warn and confirm first.
- Always confirm before any write request (POST/PUT/PATCH/DELETE); check scopes (`CLERK_BAPI_SCOPES`) before write operations rather than attempting and failing.
- Org roles in API bodies must be prefixed: `"org:admin"` / `"org:member"`.
- Paginate with `limit` + `offset`; list-users max `limit` is 500 (default 10).
- Rate limits: production 1,000 req/10s; development 100 req/10s; single invitations 100/hr; bulk invitations 25/hr; org invitations 250/hr; Frontend API sign-in creation 5/10s; sign-in attempts 3/10s.
- `currentUser()` makes a real API call that counts against rate limits — use `auth()` (session-claim read, no API call) when you only need session claims.
- `created_at` filters take `gt:`/`lt:` Unix-millisecond timestamps; ordering via `order_by` with `+`/`-` prefixed fields (e.g. `-created_at`).

**Finyte recommendations:**

- The .NET backend calls these REST endpoints directly with `CLERK_SECRET_KEY` (`snake_case` bodies). Wrap the read-spread-write metadata pattern in a helper so no code path ever does a naive metadata PATCH.
- Store Finyte-internal identifiers on Clerk users via `private_metadata`; anything the SPA needs to read (onboarding state, plan) via `public_metadata`. Never rely on `unsafe_metadata` for authorization.
- Validate the JWT locally in the .NET API; hit the Backend API only when you need data the token doesn't carry (the `auth()` vs `currentUser()` rate-limit lesson, translated).

## 1.6 clerk-cli

**Summary:** Operating the `clerk` binary (npm package `clerk`, not `@clerk/cli`) — a pre-authenticated gateway to the Backend API (`sk_...`) and Platform API (`ak_...` or OAuth), plus project tooling: `clerk auth login`, `clerk link`, `clerk env pull`, `clerk doctor`, `clerk config pull/patch/put`, `clerk api` for ad-hoc HTTP, endpoint discovery via `clerk api ls`, agent-mode behavior, key-resolution order, and recipes.

**Rules:**

- Prefer the CLI over hand-rolled `curl` — it handles auth, key resolution, app/instance targeting, and formatting.
- **Run `clerk doctor --json` first** in any session; it catches not-logged-in, not-linked, missing keys, stale versions. Exit codes: `0` success, `1` runtime error, `2` usage/validation error.
- **Discover before acting:** `clerk api ls <keyword>` before calling `clerk api <path>` — never guess endpoint paths.
- **Always `--dry-run` every mutation** (`config patch`, `config put`, `api -X POST/PATCH/PUT/DELETE`) before running it for real; in agent mode `--dry-run` is the only safety net because prompts are disabled. Mutations require `--yes` in agent mode.
- **Never run `clerk config put` without `--dry-run` first** — it's a full replacement and destructive.
- **Target explicitly in production:** pass `--instance prod` (and `--app <id>`) rather than relying on the linked default, and confirm before production mutations.
- **Never commit secrets:** `env pull` writes to `.env.local` (must be gitignored); never paste secret keys into code, chat, or logs.
- Key types are validated: Backend API needs `sk_...` (resolution: `--secret-key` flag → `CLERK_SECRET_KEY` → auto-resolve from `--app` → linked profile); Platform API needs `ak_...` (`CLERK_PLATFORM_API_KEY`) or the stored OAuth token. `config` commands are Platform-API-only and do **not** accept `--secret-key`.
- `clerk auth login` is not unattended (browser + localhost OAuth callback) — use `CLERK_PLATFORM_API_KEY` for headless automation/CI.
- Save large responses to a file and query with `jq`; `users list` returns `{data, hasMore}` (limit default 100, max 250) — walk pages with `--offset`.
- Test users (dev instances only): emails with the `+clerk_test` subaddress and US phones `+1 (XXX) 555-0100`–`555-0199` verify with fixed OTP `424242`; production rejects them; they don't count against dev caps (20 SMS, 100 emails/month).
- `clerk <command> --help` is the source of truth for flags; the installed CLI version outranks the skill doc.

**Finyte recommendations:**

- Use the CLI for all Clerk ops: `clerk env pull` for frontend keys, `clerk api` for one-off user/org fixes, `clerk config pull --output` to snapshot instance config into version control, and impersonation tokens (`clerk api /sign_in_tokens -d '{"user_id":"..."}'`) for debugging.
- Seed local/dev test households with `+clerk_test` users and OTP `424242` — pairs directly with the testing rules below.
- For CI, export `CLERK_PLATFORM_API_KEY` (Platform ops) and `CLERK_SECRET_KEY` (Backend ops).

## 1.7 clerk-testing

**Summary:** E2E testing of Clerk-authenticated apps with Playwright or Cypress — `clerkSetup()` initializes the test environment, `setupClerkTestingToken()` bypasses bot detection, `storageState` persists auth between tests.

**Rules:**

- **Never use production keys in tests** — only `pk_test_*` and `sk_test_*`.
- **Call `setupClerkTestingToken()` before navigating to auth pages** — without it, bot detection makes auth fail.
- Don't sign in through the UI in every test — persist auth with `storageState` and reuse it.
- Use `page.waitForSelector('[data-clerk-component]')` when waiting for Clerk UI to mount.
- Playwright: initialize auth state in `globalSetup`. Cypress: add `addClerkCommands({ Cypress, cy })` to the support file.
- Requires a `CLERK_TESTING_TOKEN` from the Clerk dashboard.

**Finyte recommendations:**

- Standardize on Playwright (already the plan in `PLAN_Finyte.md`): `clerkSetup()` in global setup, `setupClerkTestingToken()` per auth-touching spec, one saved `storageState` per role (household admin, household member) so multi-user scenarios don't re-authenticate through the UI.
- Seed `something+clerk_test@example.com` users on the dev instance (OTP `424242`) so E2E runs never send real email/SMS.
- Keep test-instance `pk_test_*`/`sk_test_*` keys separate from production configuration in CI.

## 1.8 clerk-webhooks

**Summary:** Clerk webhooks (delivered via Svix) — when to use them (async, eventually consistent: DB sync, notifications) and when not to (never in synchronous flows), mandatory signature verification via `verifyWebhook(req)` reading `CLERK_WEBHOOK_SIGNING_SECRET`, public webhook routes, handlers for user/org/membership sync, the event catalog, retry/replay/idempotency semantics, and local tunnel testing.

**Rules:**

- **Verify every webhook — never skip signature verification, even for notification-only handlers.** Use `verifyWebhook(req)` from the framework-specific package where one exists; do not roll your own with raw `svix` when an adapter exists.
- **Signature verification requires the raw body bytes.** In Express that means `express.raw({ type: 'application/json' })` — `express.json()` breaks verification. The same principle applies to any framework: no body parsing/model binding before verification.
- **Webhook routes must be public** — exclude them from auth middleware, otherwise Clerk gets 401.
- The signing secret lives in `CLERK_WEBHOOK_SIGNING_SECRET`; copy it to production env when the endpoint URL changes.
- **Webhooks are asynchronous and eventually consistent — never rely on delivery inside a synchronous flow.** For data the current user just created, read the session token or call the Backend API directly.
- Return **2xx to acknowledge; 4xx/5xx triggers Svix retries** on a fixed schedule. Failed webhooks can be replayed from the Dashboard.
- **Use the `svix-id` header as an idempotency key** to deduplicate retried events.
- Keep handlers fast — queue slow work and return 200 first, or you'll hit timeouts and duplicate retries.
- Handle `user.updated` (and `user.deleted`), not just `user.created`, or your mirror table drifts.
- Narrow on `evt.type` (discriminated union); payload fields are `snake_case` (`email_addresses[0].email_address`, `public_user_data.user_id`, `organization.id`, `role`).
- Local dev requires a tunnel (`ngrok`, `localtunnel`, Cloudflare Tunnel) registered as the Dashboard endpoint; Vite-based frameworks need the tunnel host in `server.allowedHosts`.
- Key events for sync: `user.created|updated|deleted`, `organization.created|updated|deleted`, `organizationMembership.created|updated|deleted`, `organizationInvitation.created|accepted|revoked`, plus session, subscription, and payment families.

**Finyte recommendations:**

- Finyte's webhook receiver lives on the .NET backend, where no Clerk `verifyWebhook` adapter exists — so the underlying rules become the spec: read the **raw request body** (no model binding before verification), verify the **Svix signature** (`svix-id`, `svix-timestamp`, `svix-signature` headers) against `CLERK_WEBHOOK_SIGNING_SECRET` using the Svix .NET library, only then deserialize; 400 on verification failure, 200 on success.
- Exclude the webhook endpoint from JWT authentication (it is Svix-signed, not user-authenticated), and record processed `svix-id`s for idempotency.
- Sync `user.created/updated/deleted` and `organizationMembership.*` into Finyte's own users/household-members tables; treat the mirror as eventually consistent — onboarding must not block on the webhook, and the backend should lazily upsert from validated JWT claims on first authenticated request.
- Do heavy work (categorization jobs, notification fan-out) off the webhook thread; ack fast.

## 1.9 Clerk cross-cutting invariants

- Secret keys (`sk_*`, `CLERK_SECRET_KEY`, `CLERK_WEBHOOK_SIGNING_SECRET`) are server-only; publishable keys (`pk_*`) are the only Clerk credentials allowed in the SPA bundle.
- Client-side auth state (`isLoaded`/`isSignedIn`, `<Show>`, `has?.()`) is UX only — the .NET backend re-checks everything from the validated session token.
- Org/household scoping comes from the session/token, never from client input; URL org identifiers must be checked against the active org.
- All Clerk metadata writes (user and org) are replace-not-merge: read, spread, write.
- Everything destructive (user delete, `config put`, agent-mode CLI mutations) gets a preview/confirmation step first.

---

# Part 2 — Fiskil Integration Model & Rules (from the guides)

## 2.1 Integration model summary

### API basics

- **Base URL:** `https://api.fiskil.com/v1`. HTTPS TLS 1.2+ only; JSON in/out; GET and POST.
- **The `/v1/` path segment is a URL namespace, NOT the API version.** The major API version (`v1` or `v2`) is selected by the **`X-Fiskil-Version`** header, falling back to the team's Console-pinned version. The per-request header always wins.
- **Auth:** exchange `client_id` + `client_secret` (Console → Settings > API Keys) at `POST /v1/token` → `{ "token", "token_type": "Bearer", "expires_in": 900 }`. Tokens live **15 minutes**; **no refresh tokens exist** — re-request on expiry. Send as `Authorization: Bearer {token}`.

### Core entities & lifecycle

- **End User** — Fiskil's representation of a Finyte user. Created via `POST /v1/end-users` with `{ email, name, phone }`. One End User can hold **multiple consents across multiple institutions** — never create per-institution End Users. All data APIs are queried by `end_user_id`. Delete End Users for churned users (recommended, and a Go-Live checklist item).
- **Auth Session** — initiates the consent flow. Created server-side via `POST /v1/auth/session` with `end_user_id` (required) and optional `redirect_uri`, `cancel_uri`, `institution_id`. Returns `session_id`, `auth_url`, `expires_at`. **Auth Sessions expire after 5 days**; the user must complete consent within that window (and each session is single-use — see Part 3).
- **Consent** — created when the user completes the flow; identified by `consent_id`. A consent can end four ways: (1) by the end user through the institution, (2) by the institution when the sharing duration expires, (3) by the institution when the user closes all accounts, (4) by you (revoke) on the user's behalf. Every ending path emits a webhook. Check active consents via `GET /v1/consent`.
- **Institution** — the data holder. `GET /v1/institutions?industry=banking&client_id={client_id}` returns `id`, `name`, `icon`, `logo`, `industry` (`banking` | `energy`), and `status.connections.status` ∈ `HEALTHY` | `DEGRADED` | `OUTAGE`.

### Consent UI / Link widget

Three integration flows, in documented order of preference:

1. **Link SDK (recommended)** — `@fiskil/link` npm package (or UMD from CDN as `FiskilLink`). `link(sessionId, options?)` returns a `LinkFlow` that is both a Promise resolving to `LinkResult { consentID?, redirectURL? }` and a controller with `.close()`. Options: `allowedOrigin` (restrict postMessage origin — recommended in production) and `timeoutMs` (default `600000` = 10 min).
2. **Redirect flow** — send the user to `auth_url`; success → `redirect_uri`, failure/cancel → `cancel_uri` with error details in query parameters.
3. **Embedded iframe flow — DEPRECATED.** Do not use.

Flow steps: institution selection → institution authentication (credentials + MFA/OTP) → account selection → consent confirmation (your branding, data types, consent period, use cases from Console "Customize UI") → completion.

**Link SDK error codes** (Promise rejects with `LinkError { name: 'LinkError', code, details? }`): `LINK_NOT_FOUND`, `LINK_TIMEOUT`, `LINK_USER_CANCELLED`, `LINK_INVALID_SESSION`, `LINK_ORIGIN_MISMATCH`, `LINK_INTERNAL_ERROR`, `CONSENT_UPSTREAM_PROCESSING_ERROR`, `CONSENT_ENDUSER_DENIED`, `CONSENT_OTP_FAILURE`, `CONSENT_ENDUSER_INELIGIBLE`, `CONSENT_TIMEOUT` (troubleshooting also mentions `AUTH_SESSION_CANCELLED`). Special case: on `LINK_INVALID_SESSION` the iframe stays mounted — call `.close()` yourself.

### Data domains

- **Banking:** Identity, Accounts, Balances, Transactions (categorised/enriched, with a `categories` object per the Fiskil Categories Taxonomy CSV), Payees, Direct Debits, Scheduled Payments, Products.
- **Energy:** Identity, Accounts, Usage (interval data — granularity varies by institution, daily vs 30-minute), Billing, Invoices, Concessions, Service Points (incl. NMI), DER, Payment Schedules, Plans.
- **Identity:** verified name, phone, email, residential address, institution metadata; retrievable per `end_user_id` after `common.identity.sync.completed`. In v2 the deprecated `customer` field is removed — use `identities[]`.
- **Income:** `GET /banking/income?end_user_id={end_user_id}` — derived from enriched transactions. Detects `WEEKLY`, `BIWEEKLY`, `MONTHLY` patterns; defaults to up to **6 months** of category `INCOME_SALARY`. Depends on `banking.transactions.basic.sync.completed` having fired.

### Webhooks

- Registered in Console under **Settings > Teams > Webhooks** (Owner or Developer role). Endpoint URL is editable later; **event subscriptions cannot be changed once created**.
- **Consent events:** `consent.received`, `consent.updated`, `consent.revoked`.
- **Identity:** `common.identity.sync.completed`.
- **Banking sync events:** `banking.accounts.sync.completed`, `banking.balances.sync.completed`, `banking.transactions.basic.sync.completed`, `banking.transactions.sync.completed` (detailed, incl. metadata), `banking.payees.sync.completed`, `banking.directdebits.sync.completed`, `banking.scheduledpayments.sync.completed`, `banking.products.sync.completed`.
- **Energy sync events:** `energy.billing.sync.completed`, `energy.usage.sync.completed`, `energy.servicepoints.sync.completed`, `energy.accounts.sync.completed`, `energy.concessions.sync.completed`, `energy.der.sync.completed`, `energy.invoices.sync.completed`, `energy.paymentschedules.sync.completed`, `energy.plans.sync.completed`.
- **Payload:** `{ delivery_attempt (1–5), publish_time (RFC3339), message_id, data: { event, end_user_id, client_id, institution_id, consent_id, account_ids?, payee_ids? } }`. `account_ids` only on `*.accounts.sync.completed`, `*.balances.sync.completed`, `banking.transactions.sync.completed`, `banking.transactions.basic.sync.completed`; `payee_ids` only on `banking.payees.sync.completed`.
- **Signature:** HMAC-SHA256 in the **`X-Fiskil-Signature`** header. Per the canonical Webhooks guide: base64-decode the signing secret, HMAC-SHA256 the raw payload bytes, base64-encode the digest, compare to the header.
- **Retries:** on non-2xx/unreachable, up to **5 attempts** with backoff of **1–10 minutes**; after that the event is **discarded permanently**.

### Testing / sandbox

The **Fiskil Sandbox** mirrors production with unlimited End Users and consents and realistic banking/energy data. Same API surface (`api.fiskil.com`) — you select the **sandbox provider** (Bank or Energy Retailer) inside the consent flow and authenticate with any reachable email via OTP. Sandbox setup/permissions go through Fiskil customer support. Auth sessions can also be created from Console (End-Users page → "Create Auth Session").

### API versioning

- `v1` and `v2` are both live; **v2 is current** for new accounts. Each major version is supported ≥ 12 months from release; deprecations are announced in the Changelog.
- Breaking changes never ship inside a major version. Breaking = removing/renaming endpoints or response fields, type changes, object↔array swaps, new required request params. Non-breaking (ships anytime) = new optional params, new response fields, new enum values, new endpoints, contract-aligning bug fixes.
- **Console pin upgrades are one-way** (v1→v2 cannot be reverted in settings).
- v1→v2 breaking changes affect only four GET responses (details in Part 3): `GET /common/identity`, `GET /banking/transactions`, `GET /banking/products/{id}`, `GET /banking/accounts`.

## 2.2 Rules

### Auth & credentials

1. **MUST** keep `client_id`/`client_secret` server-side only, in environment variables or a secrets manager. **NEVER** in frontend code, browser storage, or logs.
2. **MUST** make all Fiskil API calls from the backend. The PWA never talks to `api.fiskil.com` directly.
3. **MUST** treat tokens as 15-minute credentials: cache in memory, refresh proactively near expiry (there is no refresh-token grant — re-POST `/v1/token`), and handle `401` with automatic renewal + single retry.
4. **NEVER** log tokens, secrets, or the webhook signing secret.
5. **SHOULD** rotate API keys every 90 days or on each major product release (documented recommendation).

### Versioning

6. **MUST** send `X-Fiskil-Version` explicitly on every production request (pin the version in code, don't rely on the Console pin). Use the literal `v1` or `v2` — **NEVER** full semvers like `2.0.0` (rejected).
7. **MUST** target **v2** for Finyte (new integration; v2 is current). **NEVER** mix v1 and v2 DTO schemas in one codebase.
8. **MUST NOT** flip the Console pin before validating with the header in production — the pin change is irreversible.
9. **MUST** parse responses defensively: ignore unknown fields, accept unknown enum values (this is explicitly how Fiskil ships non-breaking changes). Concretely for .NET: no `MissingMemberHandling.Error`, no strict enum deserialization on fields like `fee_type`.

### End users & consent lifecycle

10. **MUST** persist the Fiskil end-user ID against the Finyte user record at creation time to prevent duplicate End Users. Reuse one End User for all institutions/consents.
11. **MUST** treat consent as startable only via a fresh Auth Session, and handle Auth Session expiry (5 days) by creating a new session — never reuse a stale `session_id`.
12. **MUST** handle all four consent-ending paths. Since every ending emits a webhook, drive local consent state from `consent.received` / `consent.updated` / `consent.revoked` events, and reconcile with `GET /v1/consent` when in doubt.
13. **MUST** prompt the user to reconnect when consent expires or is revoked (documented energy best practice; applies equally to banking).
14. **SHOULD** delete End Users who are no longer using the product (Go-Live checklist item).

### Webhooks

15. **MUST** verify the `X-Fiskil-Signature` HMAC-SHA256 on the **raw request body** before any processing; reject on mismatch and log the verification failure.
16. **MUST** return a 2xx **promptly** and process payloads **asynchronously** (queue/background job) — Fiskil retries only 5 times over 1–10 minute backoffs and then discards the event forever.
17. **MUST** be idempotent using `message_id` (dedupe store), because retries redeliver the same event.
18. **MUST** use HTTPS webhook endpoints (enforced in production) and **SHOULD** restrict to Fiskil IPs where possible (documented best practice; the docs don't publish the IP list — confirm with Fiskil).
19. **MUST** choose webhook event subscriptions carefully at registration time — subscriptions **cannot be changed** after creation (only the URL can). Subscribe to everything Finyte could plausibly need up front.
20. **MUST** use webhooks — not polling — as the trigger for data fetches. Specifically: no identity fetch before `common.identity.sync.completed`; no `/banking/income` call before `banking.transactions.basic.sync.completed`; no transaction fetch before the relevant `banking.transactions.*.sync.completed`.
21. **MUST NOT** rely solely on the Link SDK client callback for consent success — treat `consent.received` (server-side) as the source of truth; the client callback is UX-only.

### Data handling, pagination, errors

22. **MUST** implement pagination when fetching transaction history (cursor-based `page[*]` params — exact semantics in Part 3).
23. **MUST** use `end_user_id` as the primary query key for data APIs (documented best practice across banking/energy/identity/income).
24. The banking guide says **"do not store account or balance data — always retrieve fresh."** Finyte's household-finance model requires storing transaction history; reconcile this with rule 26's data-minimisation obligations and document Finyte's retention decision explicitly (see §2.4 ambiguities).
25. **MUST** implement upsert logic — data can change between syncs; consider soft deletes for removed records.
26. **CDR/compliance obligations (mandatory for production access):** collect only data essential to the product; implement and document a **data minimisation strategy**; establish **deletion or de-identification** processes for data no longer needed; test compliance with these; ensure identifiable customer data storage complies with **data sovereignty** regulations; configure an appropriate consent period (e.g., up to 12 months for ongoing monitoring — Finyte's case) and clearly document data usage purposes in the consent UI.
27. **MUST** handle every Fiskil error type defensively, including institution-side intermittent failures — implement retry logic for data-holder outages (Go-Live checklist). Check `status.connections.status` before presenting an institution; hide or flag `OUTAGE`.
28. **MUST** log these identifiers on every interaction (required by Fiskil Support): `end_user_id`, `consent_id`, `session_id`, `error_id`, plus webhook `message_id` and `event` name and processing timestamps. **NEVER** log sensitive payload fields.

### Sandbox vs production

29. **MUST** validate end-to-end in sandbox before production: consent flow, webhook delivery, data fetch, plus edge cases (user cancellation, auth failure, network errors, token expiry).
30. Production access **requires** completing the Console onboarding questionnaire (application + company profile + security questionnaire) and the compliance checklist. There is no separate sandbox base URL in the docs — environment separation appears to be account/provider-level (see §2.4).

## 2.3 Finyte-specific recommendations

### .NET backend (ASP.NET Core)

- **Single `FiskilClient` wrapper** (typed `HttpClient` via `IHttpClientFactory` + Polly): injects `Authorization` bearer, `X-Fiskil-Version: v2`, `Accept`/`Content-Type: application/json`. Token management as a `DelegatingHandler` or `ITokenProvider` with an in-memory cached token, refreshed ~60s before the 900s expiry, semaphore-guarded so concurrent requests don't stampede `/v1/token`. Retry once on 401 after forced refresh.
- **Config:** `Fiskil:ClientId`, `Fiskil:ClientSecret`, `Fiskil:WebhookSigningSecret` from user-secrets/env (never committed appsettings values); `Fiskil:ApiVersion` pinned to `v2`.
- **DTOs:** generate/hand-write against **v2 shapes only**: string tier bounds, `applicability_conditions` arrays, `fee_method_u_type`/`discount_method_u_type` discriminated unions, string-enum `is_activated`, `extended_data.npp_payload`, `identities[]` (no `customer`). Deserialize enums as strings with fallback-to-unknown; ignore unknown JSON members.
- **Domain mapping tables:** `finyte_users.fiskil_end_user_id` (unique), a `consents` table keyed by `consent_id` with status driven by webhook events + periodic reconciliation via `GET /v1/consent`, and a `fiskil_webhook_events` table keyed by `message_id` for idempotency/audit. (These slot into the `provider_connections` / `connection_consents` / `provider_events` tables already named in `PLAN_Finyte.md`.)
- **Webhook endpoint:** one anonymous ASP.NET Core endpoint (e.g. `POST /api/webhooks/fiskil`) that (1) reads the raw body, (2) verifies `X-Fiskil-Signature` (base64-decode secret → HMACSHA256 → base64 compare, `CryptographicOperations.FixedTimeEquals`), (3) inserts `message_id` (dedupe on unique constraint), (4) enqueues to a background channel/queue, (5) returns 200 immediately. Handlers dispatch on `data.event` and trigger the corresponding Fiskil fetch + Postgres upsert.
- **Sync orchestration:** never poll. Each `*.sync.completed` handler fetches only the affected domain for the given `end_user_id` (scoped by `account_ids` when present). Income fetch is gated on `banking.transactions.basic.sync.completed`.
- **Retention:** store transactions/derived data under a documented minimisation policy; on `consent.revoked` (and on Finyte account deletion) run the deletion/de-identification pipeline and delete the Fiskil End User.

### React PWA (consent flow)

- Since Finyte is a **web PWA**, use `@fiskil/link` directly — the WebView-bridge pattern in the Mobile Integration guide is only needed for native iOS/Android shells. If Finyte later ships a native wrapper (e.g., Capacitor), reuse the documented bridge-HTML pattern with `startLink()`/`sendToNative` verbatim.
- Flow: user taps "Link account" → PWA calls Finyte backend → backend creates End User (if needed) + `POST /v1/auth/session` → backend returns `session_id` → PWA calls `link(sessionId, { allowedOrigin, timeoutMs })` — set `allowedOrigin` in production (docs recommend it but don't state the exact origin value; verify against the `auth_url` host).
- Wrap in a `useLinkAccount` hook (pattern given in docs): statuses `idle | linking | success | error`; treat `LINK_USER_CANCELLED` as a silent return to idle; map each `LinkError.code` to the documented user-facing messages; call `.close()` on `LINK_INVALID_SESSION` and mint a fresh Auth Session.
- On resolve, send `consentID` to the backend as a UX hint only; unlock data screens off the backend's `consent.received` webhook state, showing a "syncing your accounts" state until the relevant `*.sync.completed` events land.
- Optionally build a native-feel institution picker from `GET /v1/institutions` (icons, search, health status) and pass `institution_id` into the Auth Session to skip Fiskil's picker.
- Prepare users before launching Link (explain data + purpose), show loading states, and expose consent management (view/revoke) in settings — documented UX best practices and a CDR expectation.

## 2.4 Ambiguities / gaps in the local snapshot (verify before relying on them)

1. **Webhook signature encoding conflict:** the canonical Webhooks guide uses base64-decoded secret + base64-encoded HMAC digest; the AI-tools sample uses the raw secret + hex digest. Follow the Webhooks guide; confirm against a real delivery.
2. **Auth Session response shape inconsistency:** quick-start shows `{ auth_url, expires_at (RFC3339), session_id }`; mobile-integration shows `{ id, session_id, auth_url, expires_at (unix epoch) }`. Treat the exact schema as owned by the API reference (Part 3 says `expires_at` is a Unix int).
3. **End-user create response inconsistency:** core-concepts shows `{ "end_user_id": "..." }`; the AI-tools prompt shows `{ "id": "...", email, name }`. Verify against the v2 Create end user reference.
4. **Data-fetch paths inconsistency:** the AI-tools prompt shows `GET /v1/accounts?consent_id=...`-style shorthand, but domain guides and request-log examples use `/banking/accounts`, `/banking/transactions`, `/banking/income`, `/common/identity` keyed by `end_user_id`. Trust the domain guides + API reference.
5. **Sandbox environment separation:** no distinct sandbox base URL or sandbox API keys are documented — sandbox appears to be console/provider-level. Confirm with Fiskil how sandbox vs production credentials are separated.
6. **Fiskil webhook source IPs:** "only accept webhooks from Fiskil IPs" is recommended but no IP list is published in the snapshot.
7. **Consent period / data-history limits** are configured in Console ("Customize UI"), not via API; exact allowed ranges aren't documented beyond "one-time" to "up to 12 months".
8. **Link SDK version** pinned in doc examples is `@fiskil/link@0.1.6-beta` (CDN) — beta; check the current release before committing.
9. **Snapshot freshness:** docs snapshot dates from 2026-05-21; re-run `scripts/refresh_docs.ps1` before finalising conventions.

---

# Part 3 — Fiskil API Reference Rules (for the typed .NET client)

## 3.1 API surface summary

### Base URL & auth

- **Single base URL for everything:** `https://api.fiskil.com`. HTTPS is mandatory; HTTP is rejected.
- **Token endpoint:** `POST https://api.fiskil.com/v1/token` with JSON body `{"client_id": "...", "client_secret": "..."}` (credentials go in the **body**, not Basic auth).
- **Token response:** `{ "token": "...", "expires_in": 900 }` — note the field is **`token`**, not `access_token`. Lifetime **900 s (15 min)**; re-authenticate on expiry; there is no refresh token.
- **All other calls:** `Authorization: Bearer {token}`.
- No separate sandbox base URL — sandbox is enabled per-team in Console and uses the same `api.fiskil.com` host with sandbox providers.

### Versioning mechanism

- **The `/v1/` URL segment is NOT the API version** — it is a fixed URL namespace. The API major version is selected by the **`X-Fiskil-Version` header** (values exactly `v1` or `v2`; full semvers rejected), falling back to the Console pin. Per-request header always wins.
- Console pin upgrades are **one-way**. Validate v2 with the header before flipping the pin.
- Endpoint paths and HTTP methods are **identical** in v1 and v2; only four GET response schemas differ (§3.2).

### Core endpoints (no data-domain permission; client-credential scoped)

| Method | Path | Notes |
|---|---|---|
| `POST` | `/v1/token` | Get bearer token |
| `POST` | `/v1/end-users` | Create end user (email required); returns `end_user_id` |
| `GET` | `/v1/end-users` | List/filter by `email` query param |
| `GET` | `/v1/end-users/{id}` | |
| `POST` | `/v1/end-users/{end_user_id}` | Update end user (POST, not PUT/PATCH) |
| `DELETE` | `/v1/end-users/{id}` | |
| `POST` | `/v1/auth/session` | Body: `end_user_id` (required), `redirect_uri`, `cancel_uri`, `institution_id`, `permissions[]`. Returns `id`, `session_id`, `auth_url`, `expires_at` (Unix int) |
| `GET` | `/v1/consent` | Query: `end_user_id`, `active` (bool). Returns a bare JSON **array** of consents |
| `DELETE` | `/v1/consent/{arrangement_id}` | Revoke consent |
| `GET` | `/v1/institutions` | |
| `GET` | `/v1/institutions/{id}` | Has `status` field since 2026-01-16 |
| `GET` | `/v1/permissions` | Lists Fiskil permission strings |

> **Ambiguity flag:** the generated endpoint pages' curl examples show these core paths **without** `/v1` (e.g. `https://api.fiskil.com/auth/session`), while the hand-written overview pages, quick-start and testing guides consistently use `/v1/...`. The changelog even records a past fix "Added missing /v1 to API endpoint". Treat **`/v1/...` as canonical**; verify against the live API before hardcoding.

### Banking data endpoints (all `GET` unless noted; all take `end_user_id` as required query param)

| Path | Permission | Extra query params | 200 payload root |
|---|---|---|---|
| `/v1/banking/accounts` | `accounts` | `page[*]` | `accounts[]` + `links` |
| `/v1/banking/balances` | `balances` | `account_id`, `fetch` (`fetch=true` + `account_id` = real-time fetch from data holder), `page[*]` | `balances[]` + `links` |
| `/v1/banking/transactions` | `transactions` | `account_id`, `from`, `to` (RFC3339 datetimes), `status`, `secondary_category` (repeatable, OR semantics), `page[*]` | `transactions[]` + `links` |
| `POST /v1/banking/transactions/category` | (transactions; not explicitly stated) | body: `fiskil_transaction_id`, `secondary_category` | `message` |
| `/v1/banking/payees` | `payees` | `page[*]` | `payees[]` + `links` |
| `/v1/banking/acccounts/direct-debits` | `direct_debits` | `page[*]` | `direct_debits[]` + `links` |
| `/v1/banking/payments/scheduled` | `scheduled_payments` | `page[*]` | `scheduled_payments[]` + `links` |
| `/v1/banking/products` | — (product reference data) | `instituition_id` (required — **sic**, see gotchas), `page[*]` | `products[]` + `links` |
| `/v1/banking/products/{product_id}` | — | | product detail |
| `/v1/banking/income` | (no dedicated permission documented — derived from transactions) | `from`, `to` (**`YYYY-MM-DD` dates**, default = last 6 months), `categories[]`, `account_id[]` (repeatable). **No pagination params** | `income_sources[]` + `income_summary` |

### Energy data endpoints (all `GET`, `end_user_id` required, `page[*]` supported)

| Path | Permission | Extra query params | 200 payload root |
|---|---|---|---|
| `/v1/energy/accounts` | `energy_accounts` | | `accounts[]` + `links` |
| `/v1/energy/balances` | `energy_balances` | | `balances[]` + `links` |
| `/v1/energy/billing` | `billing` | `account_id`, `invoice_number` | `billing[]` + `links` |
| `/v1/energy/invoice` | `invoices` | `account_id`, `service_point_id`, `invoice_number` | `invoices[]` + `links` |
| `/v1/energy/usage` | `usage` | `service_point_id`, `read_start_date.oldest`, `read_start_date.newest` (**`yyyy-mm-dd`**) | `usage[]` + `links` |
| `/v1/energy/concessions` | `concessions` | | `concessions[]` + `links` |
| `/v1/energy/service-points` | `service_points` | | `service_points[]` + `links` |
| `/v1/energy/payment-schedules` | `payment_schedule` (singular permission, plural path) | | `payment_schedules[]` + `links` |
| `/v1/energy/der` | `der` | | `der` data + `links` |
| `/v1/energy/plans` | — (reference data) | exactly one of `retailer_id` (CDR brand id) or `institution_id`; `status` = `active` (default) \| `all` | `plans[]` + `links` |
| `/v1/energy/plans/{plan_id}` | — | | plan detail |

### Identity & income

| Path | Permission | Params | Payload root |
|---|---|---|---|
| `GET /v1/common/identity` | `identity` | `end_user_id` (required), `institution_id` (optional). No pagination | `identities[]` (v2; the deprecated v1 `customer` field is removed) |

Income is under banking: `GET /v1/banking/income` (see banking table).

### Permission strings (exact, from `api-reference/permissions.mdx`)

- Banking: `accounts`, `balances`, `transactions`, `payees`, `direct_debits`, `scheduled_payments`
- Energy: `energy_accounts`, `energy_balances`, `usage`, `billing`, `invoices`, `concessions`, `service_points`, `der`, `payment_schedule`
- Common: `identity`

Requested via the `permissions` array on `POST /v1/auth/session`; the granted set is readable on each consent object's `permissions` field via `GET /v1/consent`.

## 3.2 Rules

### Pagination

1. **Cursor-based only.** Query params: `page[before]`, `page[after]` (opaque cursors), `page[size]` (int, **capped at 1000**, endpoint-specific default). Remember to URL-encode the square brackets.
2. **Response carries `links.prev` and `links.next` as full URIs.** Follow `links.next` verbatim rather than reconstructing cursor URLs — the docs say links are "pre-computed and guaranteed to work".
3. **Termination:** stop when the data array is empty **or** `links.next` is absent.
4. The list payload root is the **resource-named array** (`accounts`, `transactions`, `invoices`, …), not a generic `data` field — despite the pagination overview's `data` example. Model each list response as `{ <resource>: T[], links: { prev?, next? } }`.
5. Not everything paginates: `/v1/banking/income` and `/v1/common/identity` have no `page[*]` params.

### Errors & retries

1. **Error shape (4xx/5xx):** `{ "id": "err_...", "name": "...", "message": "..." }` — `name` is stable per error class (e.g. `InvalidRequest`); `message` is occurrence-specific; log `id` and include it in support tickets for 5xx.
2. Some generated endpoint pages (core resources) show an extended error object with additional booleans `fault`, `temporary`, `timeout` — model these as optional.
3. **Status codes documented:** 200, 201, 400, 401, 403, 404, 429 (rate limit exceeded), 500.
4. **Rate limits are not numerically documented for the Data API.** 429 exists in the status table and pagination docs say "avoid rapid sequential pagination requests". (The only concrete number in the docs is for the **MCP server**: 100 req/min with a `Retry-After` header — do not assume it applies to the REST API.) Implement exponential backoff on 429/5xx anyway; honor `Retry-After` if present.
5. Consent-flow error types (surface in redirect/`cancel_uri` callbacks, not REST responses): `AUTH_SESSION_INVALID`, `AUTH_SESSION_CANCELLED` (not retryable — new auth session required), `CONSENT_UPSTREAM_PROCESSING_ERROR` (retryable), `CONSENT_ENDUSER_DENIED` (retryable), `CONSENT_OTP_FAILURE`, `CONSENT_ENDUSER_INELIGIBLE`, `CONSENT_TIMEOUT`. Cancel callback format: `?error=access_denied&error_type=CONSENT_ENDUSER_DENIED&error_description=...`.
6. Requests proxied directly to data holders (URLs containing `cdr`) follow **CDR error conventions**, not the Fiskil shape.
7. The Go-live checklist explicitly requires retry/error handling for intermittent data-holder outages.

### Versioning rules for the .NET client

1. Send `X-Fiskil-Version` explicitly on **every** request (pin `v2` for Finyte) — never rely on the Console default.
2. Parse defensively: ignore unknown JSON fields; treat unknown enum values as valid (non-breaking additions ship inside a major version without notice). In .NET terms: no `MissingMemberHandling.Error`, and model enums as strings or with a fallback member.

### v1 → v2 schema differences (only these four GETs differ)

| Endpoint | Change |
|---|---|
| `GET /v1/banking/products/{id}` | `deposit_rates[]`/`lending_rates[]`: `tiers[].minimum_value` / `maximum_value` are **strings** (were numbers); `applicability_conditions` is an **array** of objects (was single object) on rates and tiers. Fees/discounts: discriminated unions — `fee_method_u_type` / `discount_method_u_type` with amount nested under `fixed_amount` (`{ "amount": "5.00" }`) or `rate_based` (`{ "rate": "0.035", "rate_type": "TRANSACTION" }`) instead of flat top-level `amount`/`balance_rate`/etc. |
| `GET /v1/banking/accounts` | Same rate/fee/discount changes as product detail, plus `features[].is_activated` is a **string enum** (e.g. `"ACTIVATED"`), not a boolean. |
| `GET /v1/banking/transactions` | `extended_data`: `nppPayload` renamed to snake_case `npp_payload`; root-level `service` and `x2p101_payload` removed; `service`/`service_version` now live under `extended_data.npp_payload`; discriminator `extension_u_type: "nppPayload"`. |
| `GET /v1/common/identity` | Deprecated `customer` field removed; `identities[]` is the canonical list (unchanged). |

Response type names in the reference reflect this: `AccountV1`/`AccountV2`, `TransactionV1`/`TransactionV2`, product detail V1/V2; all other domains share one schema.

### Webhooks (reference-level detail)

1. **Registration:** Console → Settings > Teams > Webhooks. Endpoint URL can be changed later; **event subscriptions cannot** — choose events carefully up front.
2. **Event names:** as enumerated in §2.1 (consent, identity, banking sync, energy sync families). Note `energy.balances.sync.completed` is only listed in the payload-fields section of the docs — flag it when registering subscriptions.
3. **Payload envelope:** `{ delivery_attempt (1–5), publish_time (RFC3339), message_id, data: { event, end_user_id, client_id, institution_id, consent_id, account_ids?, payee_ids? } }`. Events may include extra undocumented fields — parse leniently.
4. **Verification:** HMAC-SHA256 over the **raw request body**, key = **base64-decoded** signing secret, signature base64-encoded in the **`X-Fiskil-Signature`** header. Compare constant-time.
5. **Delivery/retry:** non-2xx or unreachable → up to **5 retries** with 1–10 min backoff, then the event is **discarded** (no dead-letter). Dedupe on `message_id` (retries reuse it), respond 2xx fast, and keep a reconciliation poll as backstop since events can be permanently lost.
6. **Data-readiness:** prefer webhooks over polling; do **not** call `/v1/banking/income` until `banking.transactions.basic.sync.completed` has fired for the consent.

### Auth-session / consent lifecycle

1. Auth sessions are **single-use** and short-lived at the flow level (~10–15 min to complete once launched; the session record itself expires after 5 days per the guides); `expires_at` is a **Unix timestamp integer** (unlike consent timestamps, which are ISO 8601 strings).
2. Success callback: `{redirect_uri}?session_id=...` — store `session_id` at creation and verify on callback.
3. `redirect_uri`/`cancel_uri` must be configured in the Fiskil Console.
4. Consent model fields to persist: `arrangement_id` (the revocation key), `active`, `permissions[]`, `expires_at`, `duration` (seconds), `account_ids[]`, `institution_id`, `institution_type` (`"banking"` / `"energy"`), `termination_reason`.

## 3.3 Gotchas

1. **Typo'd paths that are load-bearing (in the OpenAPI-derived reference):**
   - Direct debits: `GET /v1/banking/acccounts/direct-debits` — **triple-c "acccounts"** appears in both v1 and v2 pages including curl examples. Verify against the live API, but do not "fix" it silently in the client.
   - Banking products list: required query param is spelled **`instituition_id`** (not `institution_id`) on `GET /v1/banking/products`.
   - Energy invoices path is **singular**: `/v1/energy/invoice` (a 2024 changelog entry deliberately changed "invoices" → "invoice"), while its permission string is plural `invoices`.
2. **Overview pages contradict the endpoint reference on paths.** Hand-written pages show simplified paths (`/v1/accounts`, `/v1/identity`, `/v1/income`, `/v1/invoices`, `/v1/direct-debits`, `/v1/scheduled-payments`, `/v1/energy-accounts`, `/v1/payment-schedule`) that do **not** match the versioned endpoint pages (`/v1/banking/accounts`, `/v1/common/identity`, `/v1/banking/income`, `/v1/energy/invoice`, `/v1/banking/payments/scheduled`, `/v1/energy/payment-schedules`, …). The versioned `v1-0-0/`/`v2-0-0/` pages are OpenAPI-derived — treat them as canonical.
3. **Inconsistent date/time parameter conventions per endpoint:** banking transactions use RFC3339 datetimes (`from`/`to`); banking income uses `YYYY-MM-DD` dates (`from`/`to`); energy usage uses `read_start_date.oldest` / `read_start_date.newest` in `yyyy-mm-dd` (the usage overview page instead shows `from_date`/`to_date` — another overview/reference conflict). Don't build one generic date-range abstraction.
4. **Transactions date filtering semantics:** `from`/`to` match on **posted datetime first, falling back to execution datetime**; transactions with neither are **excluded entirely** from filtered results.
5. **Scheduled payments path is inverted:** `/v1/banking/payments/scheduled`, not `/banking/scheduled-payments`.
6. **Snake_case with `_u_type` discriminators** is the JSON convention (`extension_u_type`, `fee_method_u_type`, `address_u_type`, `payment_schedule_u_type`, …) — a 2023 changelog fixed `utype` → `u_type` across energy models; the v1 transaction `nppPayload` camelCase key was the outlier and is snake_cased in v2. Configure the .NET serializer for snake_case and treat `*_u_type` fields as union discriminators.
7. **Monetary/rate values are strings in v2** (`"rate": "0.0654"`, tier bounds `"59999.99"`, `fixed_amount.amount: "5.00"`) — do not map to `double`; use `decimal` parsed from string. (Legacy overview pages still show numeric balances — endpoint reference wins.)
8. **`credit_limit` is nullable** on balances; `organisation_name` is null for PERSON identities; `links.prev`/`links.next` are absent at boundaries. Nullable-annotate accordingly.
9. **No idempotency mechanism is documented** (no `Idempotency-Key` header). The only dedup handles are webhook `message_id` and single-use auth sessions. `POST /v1/end-users` retries can create duplicates — dedupe by email via `GET /v1/end-users?email=...` and by the stored `fiskil_end_user_id`.
10. **Consent list returns a bare JSON array** (no envelope, no pagination), as does `GET /v1/end-users`.
11. **Version reset in the changelog:** on **2026-04-07** Fiskil reset the Data API to "v2.0.0" with a new versioning policy; pre-reset entries (v2.0.1–v2.0.11) are historical. Deprecations recorded there that are now breaking in v2: `extended_data.service` and `extended_data.x2p101_payload` (deprecated 2025-12-18, removed in v2).
12. **Real-time balance:** `fetch=true&account_id=...` on `/v1/banking/balances` triggers a live data-holder fetch; the changelog also mentions optional `X-Fiskil-*` request headers to classify the call as consumer-present — the exact header names are **not documented** in the reference (ask Fiskil).
13. **Income endpoint defaults** to ~6 months of `INCOME_SALARY`-category data; the category taxonomy lives in a downloadable CSV (`/downloads/categories-taxonomy.csv`), and `secondary_category` values (for filtering and `POST /v1/banking/transactions/category`) must come from that taxonomy.
14. **Ambiguity — identity `customer` field:** the upgrade guide says v1 responses include a deprecated `customer` alongside `identities`, but the v1-0-0 endpoint page documents only `identities`. Irrelevant if Finyte pins v2; don't model `customer`.
15. **Ambiguity — auth session `permissions`:** the auth-session overview documents an optional `permissions` array in the request body, but the v1/v2 `create-auth-session` endpoint pages omit it. Verify whether per-session permission scoping actually works or whether permissions are Console-configured.
16. **Pagination example drift:** one overview page shows a `next` link using `offset=25` — cursor (`page[after]`) pagination per the pagination reference page is authoritative.
17. **Token response uses `token` + `expires_in` (seconds int)** — cache it and refresh proactively before the 15-minute expiry; there is no refresh token.

---

# Part 4 — Implementation Status: Rules vs. Current Code

Checked against `main@11cbc58`. This section exists so the rules above don't stay theoretical — it names the exact files that already need to change to comply with them.

## 4.1 Fiskil client (`src/Finyte.Data/ProviderSync/FiskilBankingClient.cs`, `FiskilOptions`)

| Rule | Current code | Verdict |
| --- | --- | --- |
| Golden Rule 6 / Part 2 rule 6-7 / Part 3 versioning rules: send `X-Fiskil-Version: v2` on every request | `BuildUri()` sets only `Authorization: Bearer`; no `X-Fiskil-Version` header anywhere in the client | **Gap.** Every call currently falls back to whatever version is pinned in the Fiskil Console — silently breaks the moment that pin changes, and there's no way to tell which schema (`AccountV1` vs `AccountV2`, string vs numeric rates) a response is actually in. |
| Part 2 rule 3 / Part 3 auth: exchange `client_id`/`client_secret` at `POST /v1/token`, cache the 15-minute token, refresh proactively, no refresh-token grant exists | `FiskilOptions` only has `BaseUrl` and a static `AccessToken` read straight from config (`appsettings.json`: `Fiskil:AccessToken: ""`); no token endpoint call, no expiry handling, no refresh | **Gap.** This works as a stopgap with a manually-pasted long-lived sandbox token, but it is not the documented auth model — there's no code path that would survive a production token actually expiring after 900 seconds. |
| Part 3 pagination rules: cursor-based `page[after]`/`page[size]`, follow `links.next`, resource-named array root | `GetPaged<T>()` correctly loops on `page[after]`, reads the named root (`accounts`/`balances`/`transactions`), and terminates when the next cursor is empty | **Compliant.** Good reference implementation of the pagination rules for future endpoints (payees, direct debits, energy). |
| Part 3 gotchas: typo'd paths (`acccounts/direct-debits`, `instituition_id`), string-vs-numeric monetary fields in v2 | Only `accounts`, `balances`, `transactions` are implemented so far — the typo'd endpoints (direct debits, products) and the v1/v2 rate-field schema differences haven't been exercised yet | **Watch item**, not yet a bug — flag these gotchas again when direct debits, scheduled payments, or products endpoints are added. |

**Recommended fix, in priority order:** (1) add the `X-Fiskil-Version: v2` header to every request — cheapest fix, prevents silent breakage; (2) replace the static `AccessToken` with the documented `client_id`/`client_secret` → `POST /v1/token` exchange plus an in-memory, semaphore-guarded, proactively-refreshed cache, per the `ITokenProvider` pattern in Part 2.3.

## 4.2 Fiskil webhook endpoint (`src/Finyte.Api/Endpoints/FiskilWebhookEndpoints.cs`, `FiskilWebhookIngestor`)

| Rule | Current code | Verdict |
| --- | --- | --- |
| Golden Rule 3 / Part 2 rule 15 / Part 3 webhook verification: verify `X-Fiskil-Signature` HMAC-SHA256 on the raw body before processing | `HandleWebhook` reads the raw body, deserializes it, and calls the ingestor — **no signature check exists anywhere in the endpoint or the ingestor**. There is no `Fiskil:WebhookSigningSecret` entry in `appsettings.json` at all (only `BaseUrl` and `AccessToken` under `Fiskil:`) | **Live security gap.** `POST /api/provider-sync/fiskil/webhook` is `.AllowAnonymous()` (correct — Fiskil can't send a user JWT) but currently has **no authentication of any kind**. Anyone who finds the URL can POST a fabricated event; if `data.end_user_id` matches an existing `ProviderConnection`, it queues a real `ProviderSyncRun` against that tenant. This should be fixed before any real (non-sandbox) Fiskil credentials are wired up. |
| Part 2 rule 17 / Part 3: dedupe on `message_id` | `Ingest()` checks `(Provider, MessageId)` before inserting and returns `IsDuplicate: true` on a repeat | **Compliant.** |
| Part 2 rule 16: ack fast, process async | Handler inserts a `ProviderWebhookEvent` + a `Queued` `ProviderSyncRun` and returns — it does not fetch data inline | **Compliant on the "fast ack" half.** But the actual data-fetch never happens automatically: `POST /api/provider-sync/runs/{id}/run` is the only thing that executes a queued run, and it's gated `environment.IsDevelopment()`-only (returns 404 outside Development). **There is currently no background worker or Quartz job that drains `Queued` sync runs in a deployed environment** — every webhook event correctly gets recorded, but nothing production-side acts on it yet. This is an incompleteness (Phase 4/5 in `PLAN_Finyte.md` are still open), not a contradiction of a rule. |

**Recommended fix, in priority order:** (1) add `Fiskil:WebhookSigningSecret` to configuration and verify `X-Fiskil-Signature` (base64-decoded secret, HMAC-SHA256 over the raw body, base64-encoded compare, constant-time) before touching the deserialized payload — reject with 400 on mismatch; (2) add a background job (Quartz, per `PLAN_Finyte.md`) that picks up `Queued` `ProviderSyncRun` rows outside of Development, replacing the dev-only manual trigger endpoint.

## 4.3 Tenancy model vs. the Clerk Organizations recommendation (§1.4)

Part 1.4 recommended Clerk **Organizations** as the household model (`org:admin`/`org:member`, `<OrganizationSwitcher>`, org-claim-derived tenant scoping). The actual implementation (`src/Finyte.Api/Tenancy/TenantResolver.cs`) took a different, simpler path: on first authenticated request, Finyte auto-creates its **own** `Tenant`/`TenantMember` row per individual Clerk user (`Role = Owner`, tenant name `"My household"`), keyed off the Clerk `sub` claim — not off any Clerk org ID. `TenantRole` already models an `Owner` vs. other-role distinction, but nothing yet invites a second Clerk user into an existing tenant.

This isn't wrong for a single-user-per-household v1, and the core security invariant from §1.4 still holds either way (every endpoint derives tenant/household ID from server-side state tied to the authenticated user, never from client input). But it means:

- The Clerk-orgs rules in §1.4 (permission strings, `orgSlug` checks, `<OrganizationSwitcher>`) are **not currently applicable** — there's no Clerk Organization in play, only Finyte's own tenant table.
- If/when Finyte adds multi-member households, decide explicitly whether to (a) build invite/membership on top of the existing `TenantMember` table, or (b) switch to Clerk Organizations as originally recommended and migrate `TenantResolver` to read the org claim instead of auto-creating a tenant per user. Don't let both models exist in parallel.

---

## Open items to resolve before production

Collected from all three parts — each needs confirmation against the live APIs or the vendor before Finyte onboards real users:

1. Confirm the Fiskil webhook signature encoding (base64 vs hex) against a real sandbox delivery.
2. Confirm whether `POST /v1/auth/session` accepts a `permissions[]` array or whether permissions are Console-only.
3. Confirm the canonical `/v1` prefix on core endpoints, the `acccounts` direct-debits path, and the `instituition_id` param against the live API.
4. Get the Fiskil webhook source IP list (or accept signature-only verification).
5. Confirm sandbox vs production credential separation with Fiskil.
6. Confirm the consumer-present `X-Fiskil-*` headers for real-time balance fetches.
7. Decide and document Finyte's transaction retention policy versus the banking guide's "do not store account or balance data" advice and CDR minimisation rules (feeds `PLAN_Finyte.md` Phase 0 legal work).
8. Pin which Clerk Core generation (`@clerk/react` v7+/Core 3 vs Core 2) the project is on and record it in §1.3.
9. Check the current `@fiskil/link` release (docs pin `0.1.6-beta`).
10. Re-run the Fiskil docs refresh script (snapshot is from 2026-05-21) before implementing the typed client.
11. **(Highest priority — live gap, see §4.2)** Add `X-Fiskil-Signature` verification to `FiskilWebhookEndpoints.HandleWebhook` before any non-sandbox Fiskil credentials are configured — the endpoint currently accepts unsigned, unauthenticated POSTs.
12. **(See §4.1)** Add the `X-Fiskil-Version: v2` header to `FiskilBankingClient` and replace the static `Fiskil:AccessToken` config value with the documented `client_id`/`client_secret` → `POST /v1/token` exchange and refresh cycle.
13. **(See §4.2)** Add a background job to drain `Queued` `ProviderSyncRun` rows outside Development — today only a dev-only manual-trigger endpoint runs them.
14. **(See §4.3)** Decide whether household multi-membership will be built on Finyte's own `Tenant`/`TenantMember` model or migrated to Clerk Organizations, and update §1.4 once decided — right now the code and the original recommendation disagree.
