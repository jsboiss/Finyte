# Finyte Mobile UI Plan

## Goal

Turn the current desktop-oriented dashboard shell into a mobile-first finance app UI, delivered as an installable PWA, without abandoning desktop. This plan covers the frontend only (`src/Finyte.Web`); backend phases continue to follow `PLAN_Finyte.md`.

`PLAN_Finyte.md` already makes two decisions this plan builds on:

- The first release is a **PWA**, not a native app (Phase 2, "Recommended decision").
- Phase 6 defines the minimum useful screens: dashboard overview, accounts, account detail, transactions, upcoming payments, bills, and sync status.

This plan turns those into a concrete mobile UI architecture and build order.

## Current State (audit)

**Updated after `main@11cbc58` ("0000 working branch #4").** The original audit below was written against `main@00acb4b`; a large feature commit landed since then (tenancy, Stripe billing, dashboard projections, Fiskil sync) and changed the frontend substantially. Re-check this table again before starting M1, since the app is moving fast.

What exists today in `src/Finyte.Web` (as of `main@11cbc58`):

| Area | State |
| --- | --- |
| Stack | React 19, TypeScript, Vite 8, TanStack Router/Query/Table, Clerk, Orval, Axios — unchanged. |
| Styling | Tailwind v4 is still installed but effectively unused; `app/App.css` has grown from ~220 to **~680 lines** of bespoke CSS (billing panels, overview cards, chart styling). shadcn/ui is still not installed. |
| Layout | Still a fixed grid shell (`.app-shell`, `248px` sidebar + content), but a **first-pass responsive breakpoint has already been added ad hoc**: `@media (max-width: 800px)` in `app/App.css` collapses `.app-shell` to one column, shrinks the sidebar nav to an icon-only 3-column grid, and stacks the metric/billing/overview grids to 1 column. This is a stopgap, not the mobile-first shell this plan calls for (no bottom tab bar, no safe-area handling, no shadcn/ui, breakpoint is `max-width` px-based rather than Tailwind's `md` token) — **M1 should replace it, not build alongside it.** |
| Routing | Four routes now, still all in one file, `app/App.tsx` (grown from 217 to **~660 lines**): `/`, `/connections`, `/billing` (new), `/settings`. The M1 file-split is more overdue than before. |
| Theme | Dark-only — unchanged. |
| Data | The dashboard (`DashboardPage`) now renders a real overview projection (`OverviewResponse`): account balance, current-month spend, average daily spend, an income-vs-expense "cash flow race" bar, a daily cash-flow bar chart (`DailyCashFlowChart`), and a spend-by-tag pie chart (`SpendByTagChart`, CSS `conic-gradient` — **no charting library was added**, these are hand-rolled with divs). A `LockedDashboard` state gates the overview behind billing access. Backend now also has real tenancy (`Finyte.Core.Tenancy`), Stripe billing (`Finyte.Api.Billing`), and Fiskil sync (`Finyte.Data.ProviderSync`) — see `neko_fable.md` for provider-integration rules and known gaps. |
| Billing | New: a `/billing` route with a plan picker (Monthly/Yearly) and a `BillingAccessPanel` that calls Stripe checkout/portal session endpoints. Entirely unstyled for mobile — same fixed-grid treatment as everything else. |
| Auth mode | New: a `VITE_DEV_AUTH` toggle switches between the real `ClerkDashboardShell` and a `SignedInShell` that skips Clerk entirely (backed by a server-side `DevAuthenticationHandler` + `DevData` seeding, dev-only). Relevant to how M1–M3 get tested locally without needing live Clerk sign-in. |
| PWA | Still nothing: no manifest, no service worker, no app icons, no theme-color meta. |
| Mobile hygiene | Still just the viewport meta plus the new ad hoc `max-width: 800px` breakpoint above; no safe-area handling, no touch-target sizing audit, tables are still raw `<table>` elements. |

Conclusion: the mobile-first shell, design tokens, and PWA work this plan describes are **still entirely unstarted** — nothing here supersedes M1–M6. But the surface area to rebuild has grown (billing screen, real overview charts, tenancy-aware dashboard), and someone has already started patching in responsiveness ad hoc at the CSS level. Start M1 by replacing that breakpoint with the real mobile-first shell rather than extending it, and fold Billing into M3 (see below) since it didn't exist when this plan was first written.

## Guiding Decisions

1. **Mobile-first responsive PWA, one codebase.** No React Native, no separate mobile app. Design every screen at 360–430px first; desktop is the progressive enhancement (sidebar appears, lists can become tables).
2. **Bottom tab bar on mobile, sidebar on desktop.** The same route tree drives both; only the shell chrome changes at the breakpoint (`md`/768px).
3. **Tailwind utilities + shadcn/ui as the component base.** Retire `App.css` bespoke classes as screens are rebuilt. shadcn/ui was already the plan in `PLAN_Finyte.md`; its primitives (Sheet, Drawer, Dialog, Tabs, Skeleton, Card) map directly onto mobile patterns.
4. **Cards and lists on mobile, tables on desktop.** TanStack Table stays for wide viewports; the same column/model definitions feed a card-list renderer under `md`. Never ship a horizontally-scrolling data table as the primary mobile experience.
5. **Keep dark mode as default, add proper theme tokens.** Move the hardcoded hex palette into Tailwind theme variables (`@theme` in Tailwind v4) so light mode can be added later without a rewrite.
6. **File-based route organization.** Split `App.tsx` into route files under `app/routes/` before adding more screens. Every new Phase 6 screen gets its own file, loading/empty/error states included.
7. **Rebuild the hand-rolled overview charts as responsive components, not a charting library, for now.** `CashFlowRace`, `DailyCashFlowChart`, and `SpendByTagChart` (`app/App.tsx`) are already CSS/div-based (bar heights, `conic-gradient` pie) with no dependency — keep that approach and make it responsive in M2/M3 rather than introducing a charting library mid-build. Revisit a real charting library only if hand-rolled charts can't cover future Phase 9 analytics needs.

## Phase M1: Responsive App Shell

Goal: the existing four pages (Dashboard, Connections, Billing, Settings) work well on a phone.

Deliverables:

- Split `App.tsx`: shell layout, auth gate, and each page into separate files (`app/routes/`, `app/components/layout/`).
- Remove the ad hoc `@media (max-width: 800px)` breakpoint in `app/App.css` as part of the split — it's superseded by the shell below, not layered under it.
- New shell layout:
  - `< md`: sticky top header (brand, page title, `UserButton`) + fixed bottom tab bar with 4 tabs: **Home, Accounts, Bills, Settings** (Connections and Billing move under Settings on mobile; keep them as top-level sidebar items on desktop).
  - `>= md`: current sidebar pattern, rebuilt with Tailwind utilities.
- Install and configure shadcn/ui; adopt its Button, Card, Skeleton, Sheet components in the shell.
- Mobile hygiene baseline:
  - `viewport-fit=cover` + `env(safe-area-inset-*)` padding on header and tab bar (iOS notch/home indicator).
  - Minimum 44×44px touch targets for all interactive elements.
  - `theme-color` meta matching the dark background (stop the white flash noted in the darkmode commit).
  - No horizontal page scroll at 360px — enforce with a quick Playwright viewport check.
- Convert the dashboard "setup progress" table to a card list on mobile.

Acceptance:

- All existing pages usable at 360px and 1280px with no horizontal scroll.
- Bottom tabs navigate between routes; active state is visible; back button works.
- Sign-in page (Clerk `<SignIn />`) renders correctly on mobile.

## Phase M2: Design Tokens And Core Components

Goal: a small, consistent component kit so Phase 6 screens are assembly, not invention.

Deliverables:

- Tailwind `@theme` tokens for color (background, surface, border, text, positive/negative amounts, accent), radius, and spacing — sourced from the current `App.css` palette.
- Money formatting utility: single `formatMoney(amount, currency)` used everywhere; negative amounts styled consistently (color + sign), AUD default per the backend model.
- Core components:
  - `AmountText` (signed, colored, tabular-nums).
  - `StatCard` (label + big number + optional delta) — replaces `.metric-grid`.
  - `ListRow` (icon/avatar, title, subtitle, trailing amount/chevron) — the workhorse for accounts, transactions, and bills on mobile.
  - `EmptyState`, `ErrorState`, `Skeleton` list variants — every data screen must ship all three states (Phase 6 requirement in `PLAN_Finyte.md`).
  - `BottomSheet` (shadcn Drawer) for mobile filters and row detail actions.
- Delete the now-unused parts of `App.css`; target is to remove the file entirely by end of M3.

Acceptance:

- Dashboard, Connections, Billing, Settings rebuilt on the kit with zero bespoke-CSS classes.
- A visual pass at 360/390/768/1280px shows consistent spacing, type scale, and touch targets.

## Phase M3: Money Screens (aligns with PLAN_Finyte Phase 6)

Goal: the core mobile finance experience against real API data.

Screens, in build order:

1. **Home (dashboard)** — rebuild the existing overview projection (`OverviewResponse`: account balance, current-month spend, average daily spend, cash-flow race, daily cash-flow bars, spend-by-tag pie) as mobile-first components on the M2 kit, plus upcoming payments preview, recent transactions preview, and sync status line. Preserve the existing `LockedDashboard` gate behavior for users without billing access. Everything links through to its full screen.
2. **Accounts list** — `ListRow` per account (name, product, masked number, balance) from the existing `GET /api/accounts`. Pull-to-refresh via TanStack Query refetch.
3. **Account detail** — balance header (current/available/limit), transaction list for the account, metadata section.
4. **Transactions** — infinite-scrolling list grouped by date (virtualized once counts grow), search box, filter bottom-sheet (account, date range, category). Desktop `>= md` renders the same data as a TanStack Table with sorting/pagination.
5. **Bills / Upcoming** — timeline of upcoming payments, direct debits, and bill due dates. Blocked on backend Phase 4/6 endpoints; build with the empty state first so the tab exists from M1.
6. **Billing** — rebuild the existing plan picker and `BillingAccessPanel` (checkout/portal session buttons, subscription status) as a mobile settings-style screen; single-column card layout, no grid.

Backend dependencies (coordinate with `PLAN_Finyte.md` phases — build UI behind empty states until ready):

- Transactions list endpoint (entity exists; endpoint does not) — dashboard/overview and billing already have working endpoints, so screens 1 and 6 are **not** blocked.
- Upcoming payments / direct debits endpoints (Phase 4 Fiskil spike).
- Connections list/status endpoints (Phase 5).

Rules:

- Every screen ships loading (skeleton), empty, and error states in the same PR as the happy path.
- All list screens work one-handed: primary actions reachable in the bottom half, filters in bottom sheets, detail navigation by row tap.
- Amounts always via `AmountText`; dates via one shared formatter.

Acceptance:

- Matches `PLAN_Finyte.md` Phase 6 acceptance on a phone: a user can see accounts, balances, transactions, and upcoming payments (or honest empty states where the backend isn't there yet).

## Phase M4: Connections And Settings On Mobile

Goal: the self-service flows from `PLAN_Finyte.md` Phase 5 work on a phone.

Deliverables:

- Connections screen: provider cards with status, data domains, last sync, and actions (refresh, reconnect, disconnect — destructive actions confirmed via bottom sheet).
- Fiskil Link / consent flow tested on mobile viewports specifically — consent UIs are where mobile breaks first. Follow `.codex/skills/fiskil-api` docs for the Link SDK.
- Import status: queued/running/failed visible per connection with a retry action (Phase 5 operational behavior).
- Settings: account (Clerk profile/security), appearance, legal links, sign out, delete-account entry point.

Acceptance:

- A user can start, complete, and disconnect a sandbox bank connection entirely from a phone.

## Phase M5: PWA Installability

Goal: Finyte installs to the home screen and feels like an app.

Deliverables:

- Web app manifest: name, short_name, icons (192/512 + maskable), `display: standalone`, `background_color`/`theme_color` matching dark theme.
- Service worker (via `vite-plugin-pwa`): precache the app shell, offline fallback page. **Do not cache API responses containing financial data** in v1 — offline shows shell + "reconnect" state, not stale balances (avoids the data-retention questions flagged in the plan's privacy section).
- iOS specifics: `apple-touch-icon`, standalone-mode safe-area verification, no 300ms-tap or double-tap-zoom issues.
- Install prompt affordance on the Settings screen.
- Update flow: new deploys prompt "refresh to update" rather than silently serving stale bundles.

Acceptance:

- Lighthouse PWA checks pass; installed app on iOS Safari and Android Chrome launches standalone into the dashboard with correct theming and safe areas.

## Phase M6: Polish And Confidence

- Route-level code splitting so first paint on mobile networks is fast; set a bundle budget (< 250KB gzipped initial JS as a starting target).
- Virtualize the transaction list once real data volumes arrive.
- Reduced-motion support; focus-visible styles; screen-reader labels on tab bar and amount signs (a11y pass).
- Playwright viewport suite (360px and 1280px) covering: sign-in, dashboard, accounts, transactions, connections, billing — this satisfies the "minimal Playwright coverage" testing requirement in `PLAN_Finyte.md`.
- Optional: light theme via the tokens laid down in M2.

## Screen ↔ Navigation Map

```
Bottom tabs (mobile)          Sidebar (desktop)
├── Home        /             ├── Dashboard    /
├── Accounts    /accounts     ├── Accounts     /accounts
│   └── /accounts/$accountId  │   └── Account detail
├── Bills       /bills        ├── Transactions /transactions
└── Settings    /settings     ├── Bills        /bills
    ├── Connections           ├── Connections  /connections
    │   /connections          ├── Billing      /billing
    └── Billing /billing      └── Settings     /settings

Transactions on mobile is reached from Home ("see all") and
Account detail; it does not need its own tab. Connections and
Billing are both settings-adjacent on mobile — surface them as
rows within Settings rather than adding more tabs.
```

## What Not To Do Yet

- No offline caching of financial data.
- No charting library — the existing overview charts (`CashFlowRace`, `DailyCashFlowChart`, `SpendByTagChart`) are hand-rolled CSS/div components; keep that approach through M3 and only reconsider a library if Phase 9 analytics outgrows it.
- No push notifications (needs backend + product decisions; revisit with Phase 9 "alerts and notifications").
- No gesture-heavy interactions (swipe-to-categorize etc.) before the basics are solid.
- Don't keep growing `App.tsx` — M1's split is a prerequisite for everything else, and it's more overdue now than when this plan was written (~660 lines and counting).

## Build Order Summary

1. **M1** Responsive shell + bottom tabs + route file split *(unblocks everything; no backend needed)*
2. **M2** Tokens + component kit *(no backend needed)*
3. **M3** Home, Accounts, Account detail, Transactions, Bills *(needs transactions endpoint; bills UI behind empty state)*
4. **M4** Connections + Settings flows *(needs Phase 4/5 backend)*
5. **M5** PWA installability
6. **M6** Performance, a11y, Playwright coverage

M1 and M2 can start immediately against the current API surface.
