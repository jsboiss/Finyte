# Finyte Mobile UI Plan

## Goal

Turn the current desktop-oriented dashboard shell into a mobile-first finance app UI, delivered as an installable PWA, without abandoning desktop. This plan covers the frontend only (`src/Finyte.Web`); backend phases continue to follow `PLAN_Finyte.md`.

`PLAN_Finyte.md` already makes two decisions this plan builds on:

- The first release is a **PWA**, not a native app (Phase 2, "Recommended decision").
- Phase 6 defines the minimum useful screens: dashboard overview, accounts, account detail, transactions, upcoming payments, bills, and sync status.

This plan turns those into a concrete mobile UI architecture and build order.

## Current State (audit)

What exists today in `src/Finyte.Web`:

| Area | State |
| --- | --- |
| Stack | React 19, TypeScript, Vite 8, TanStack Router/Query/Table, Clerk, Orval, Axios |
| Styling | Tailwind v4 is installed and imported (`app/index.css`) but the UI is styled with ~220 lines of bespoke CSS in `app/App.css`. shadcn/ui is planned in `PLAN_Finyte.md` but not installed. |
| Layout | Fixed two-column grid: `248px` sidebar + content (`app/App.css` `.app-shell`). Unusable on a phone — the sidebar alone eats most of a 390px viewport. |
| Routing | All three routes (`/`, `/connections`, `/settings`) and every page component live in one file, `app/App.tsx`, using code-based route definitions. |
| Theme | Dark-only (`color-scheme: dark`, hardcoded hex palette). |
| Data | Generated client (`app/api/generated/finyteApi.ts`) exposes app status, current user, and account list/create. Transactions exist in the backend (`Finyte.Core/Accounts/Transaction.cs`) but have no endpoint yet. |
| PWA | Nothing: no manifest, no service worker, no app icons, no theme-color meta. |
| Mobile hygiene | Viewport meta exists; no safe-area handling, no touch-target sizing, tables are raw `<table>` elements that will overflow small screens. |

Conclusion: this is early enough that we should restructure toward mobile-first now, before Phase 6 screens multiply, rather than retrofit later.

## Guiding Decisions

1. **Mobile-first responsive PWA, one codebase.** No React Native, no separate mobile app. Design every screen at 360–430px first; desktop is the progressive enhancement (sidebar appears, lists can become tables).
2. **Bottom tab bar on mobile, sidebar on desktop.** The same route tree drives both; only the shell chrome changes at the breakpoint (`md`/768px).
3. **Tailwind utilities + shadcn/ui as the component base.** Retire `App.css` bespoke classes as screens are rebuilt. shadcn/ui was already the plan in `PLAN_Finyte.md`; its primitives (Sheet, Drawer, Dialog, Tabs, Skeleton, Card) map directly onto mobile patterns.
4. **Cards and lists on mobile, tables on desktop.** TanStack Table stays for wide viewports; the same column/model definitions feed a card-list renderer under `md`. Never ship a horizontally-scrolling data table as the primary mobile experience.
5. **Keep dark mode as default, add proper theme tokens.** Move the hardcoded hex palette into Tailwind theme variables (`@theme` in Tailwind v4) so light mode can be added later without a rewrite.
6. **File-based route organization.** Split `App.tsx` into route files under `app/routes/` before adding more screens. Every new Phase 6 screen gets its own file, loading/empty/error states included.

## Phase M1: Responsive App Shell

Goal: the existing three pages work well on a phone.

Deliverables:

- Split `App.tsx`: shell layout, auth gate, and each page into separate files (`app/routes/`, `app/components/layout/`).
- New shell layout:
  - `< md`: sticky top header (brand, page title, `UserButton`) + fixed bottom tab bar with 4 tabs: **Home, Accounts, Bills, Settings** (Connections moves under Settings on mobile; keep it as a top-level sidebar item on desktop).
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

- Dashboard, Connections, Settings rebuilt on the kit with zero bespoke-CSS classes.
- A visual pass at 360/390/768/1280px shows consistent spacing, type scale, and touch targets.

## Phase M3: Money Screens (aligns with PLAN_Finyte Phase 6)

Goal: the core mobile finance experience against real API data.

Screens, in build order:

1. **Home (dashboard)** — total balance headline, per-account balance summary, upcoming payments preview, recent transactions preview, sync status line. Everything links through to its full screen.
2. **Accounts list** — `ListRow` per account (name, product, masked number, balance) from the existing `GET /api/accounts`. Pull-to-refresh via TanStack Query refetch.
3. **Account detail** — balance header (current/available/limit), transaction list for the account, metadata section.
4. **Transactions** — infinite-scrolling list grouped by date (virtualized once counts grow), search box, filter bottom-sheet (account, date range, category). Desktop `>= md` renders the same data as a TanStack Table with sorting/pagination.
5. **Bills / Upcoming** — timeline of upcoming payments, direct debits, and bill due dates. Blocked on backend Phase 4/6 endpoints; build with the empty state first so the tab exists from M1.

Backend dependencies (coordinate with `PLAN_Finyte.md` phases — build UI behind empty states until ready):

- Transactions list endpoint (entity exists; endpoint does not).
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
- Playwright viewport suite (360px and 1280px) covering: sign-in, dashboard, accounts, transactions, connections — this satisfies the "minimal Playwright coverage" testing requirement in `PLAN_Finyte.md`.
- Optional: light theme via the tokens laid down in M2.

## Screen ↔ Navigation Map

```
Bottom tabs (mobile)          Sidebar (desktop)
├── Home        /             ├── Dashboard    /
├── Accounts    /accounts     ├── Accounts     /accounts
│   └── /accounts/$accountId  │   └── Account detail
├── Bills       /bills        ├── Transactions /transactions
└── Settings    /settings     ├── Bills        /bills
    └── Connections           ├── Connections  /connections
        /connections          └── Settings     /settings

Transactions on mobile is reached from Home ("see all") and
Account detail; it does not need its own tab.
```

## What Not To Do Yet

- No React Native / Capacitor / App Store builds — PWA first, per `PLAN_Finyte.md`.
- No offline caching of financial data.
- No charts library yet — Phase 6 is deliberately tables/lists/summaries; charts arrive with Phase 9 analytics migration.
- No push notifications (needs backend + product decisions; revisit with Phase 9 "alerts and notifications").
- No gesture-heavy interactions (swipe-to-categorize etc.) before the basics are solid.
- Don't keep growing `App.tsx` — M1's split is a prerequisite for everything else.

## Build Order Summary

1. **M1** Responsive shell + bottom tabs + route file split *(unblocks everything; no backend needed)*
2. **M2** Tokens + component kit *(no backend needed)*
3. **M3** Home, Accounts, Account detail, Transactions, Bills *(needs transactions endpoint; bills UI behind empty state)*
4. **M4** Connections + Settings flows *(needs Phase 4/5 backend)*
5. **M5** PWA installability
6. **M6** Performance, a11y, Playwright coverage

M1 and M2 can start immediately against the current API surface.
