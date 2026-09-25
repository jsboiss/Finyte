# Budgets

Budgets are family-owned spending limits, available at `/budgets` with an active subscription. The branch builds on account classification/preferences and internal transfer review. Finance's weekly category/tag budget was a prototype; Finyte makes period boundaries, inclusion rules, and the transactions behind each total explicit.

## Counting rules

- A budget has exactly one currency. Only transactions with that currency count, even when their account reports a different currency. No exchange-rate conversion occurs.
- Actual spending is the positive total of negative, dated, posted transactions through the current UTC calendar day. Legacy null/empty statuses count as posted, consistent with other Finyte analytics. Pending, undated, future-dated, zero and positive transactions do not count. Credits are not inferred to be refunds.
- Valid confirmed internal transfers are excluded using the shared transfer query. A stale confirmation stops excluding the corrected transaction until reviewed again.
- `matchMode: all` explicitly means all eligible spending. `selected` matches exact primary or secondary category names (case insensitive), **or** any selected tag. A transaction matching several categories/tags counts once. Category matching is not a merchant or substring search.
- `accountScope: analytics` follows account preferences, including accounts added later. `selected` counts only those accounts, overriding their combined-analytics preference as direct account views do. Transaction currency filtering still applies.
- Deleted tag/account links cascade away, while the explicit scope persists. Deleting the last selected tag or account cannot broaden a budget to all transactions. A now-empty selection counts nothing and the UI explains why.
- Budgets may overlap. Their totals are not additive.
- Spending is gross, never net of refunds. Refunding a budgeted purchase leaves the original debit counted and records the refund as a separate credit outside the budget, so a period's spending never changes after the fact. This is a decided position, recorded against issue #38 on 25 September 2026, not a limitation waiting to be fixed: it keeps every total traceable to transactions with a matching account, date, currency and transfer scope. A net-of-refund mode would have to be added as an explicit, opt-in reporting choice.

## Periods and history

Weekly and fortnightly schedules repeat every 7 or 14 days from a known starting date. Monthly schedules recur on the anchor's day, clamping to the last day of shorter months without drifting: January 31 -> February 28/29 -> March 31. The schedule extends before the anchor. Period endpoints shown to users are inclusive; database queries use an exclusive next-day boundary. Dates use UTC calendar days.

Each period gets the full current limit; there is no carry-forward or proration. Negative remaining amounts show an overspend. `observedThrough` is the last UTC day with eligible observations, or null for a future period. Selecting an old date chooses a period, **not** an immutable historical snapshot. History is recalculated from current transactions, tags, account preferences and budget settings. This makes provider corrections and transfer reviews visible immediately.

## API

All routes require an authenticated family and subscription; family IDs are resolved from identity and never accepted in the request. Amounts are decimal **major currency units**, unlike older dashboard fields named `MinorUnits`.

| Method and path | Behaviour |
| --- | --- |
| `GET /api/budgets` | Definitions, scopes and versions; sorted by name then ID. |
| `GET /api/budgets/categories` | Available primary/secondary names from this family’s transactions, deduplicated ignoring case. Empty, padded, and overlength names that cannot be saved are omitted. |
| `POST /api/budgets/preview?date=2026-09-22` | Validate an unsaved definition and return its containing period, observed-through date, matching spent/count, excluded currencies, and up to five latest example transactions. Does not write a budget. Uses the saved-budget eligibility query in a repeatable-read snapshot. |
| `POST /api/budgets` | Create a definition. Returns 201, initial version 0. |
| `PUT /api/budgets/{id}` | Replace settings atomically. Requires `expectedVersion`; increments version. |
| `DELETE /api/budgets/{id}?expectedVersion=0` | Delete a definition and its selection links only; preserves transactions. |
| `GET /api/budgets/{id}/periods?date=2026-09-08&count=6` | Containing period first, then prior periods. Defaults to UTC today and six; count 1–12. Includes limit, spent, remaining, usedPercent, transactionCount and observedThrough. |
| `GET /api/budgets/{id}/transactions?date=2026-09-08&page=1&pageSize=25` | Paginated audit using exactly the same inclusion query as summaries. Count before paging; posted timestamp descending then ID. Date required, size 1–100. Ledger amounts remain negative. |

Example definition:

```json
{
  "name": "Food",
  "limit": 250.00,
  "currency": "AUD",
  "frequency": "fortnightly",
  "anchorDate": "2026-09-04",
  "matchMode": "selected",
  "categories": ["Groceries", "Eating Out"],
  "tagIds": [],
  "accountScope": "analytics",
  "accountIds": []
}
```

PUT adds `expectedVersion`. Limits must be positive with at most two decimal places and fit in 16 whole digits. Names/categories have at most 120 characters, currency is three letters, dates span 1901–9990, selections are capped at 50 categories/tags and 100 accounts. Unavailable category names and nonexistent or foreign tags/accounts are rejected instead of silently discarded. Existing budgets keep their explicit selected scope if a category disappears; edit the unavailable selection before saving again. Selected scopes require at least one criterion/account at save time; all/analytics scopes reject contradictory selections. A family may create up to 100 budgets. Names are labels, not identifiers, and need not be unique. Malformed requests return 400, absent/foreign budget IDs 404, stale edits/deletes 409, inactive subscriptions 402.

## Storage and validation

`AddBudgets` creates `budgets`, `budget_tags`, and `budget_accounts`. Definition version is an EF concurrency token. Settings and selection changes are saved together. PostgreSQL reads use a repeatable-read snapshot so configuration, scope, totals and pagination within one response remain consistent during concurrent syncs or edits. Summary aggregation runs in PostgreSQL; the audit applies count, ordering and pagination server-side, without loading complete transaction histories or tag arrays into the response.

`BudgetApiTests` exercises anchor boundaries (including leap/month-end/backwards periods), overlap de-duplication, tenant/currency/account scoping, transfer corrections, pending/undated/future exclusions, exact-category matching, validation, concurrency versions, and complete paginated audit totals. For real PostgreSQL coverage, set `FINYTE_TEST_POSTGRES` to a **test** connection string and run `dotnet test --filter BudgetApiTests`. The opt-in test migrates and removes its own random schema, leaving the database's existing schemas and rows intact. It checks relational translation, all migrations, counts/pagination and tag/account cascade behaviour.

Budgets calculate live; no Temporal workflow or projection migration is required. The placeholder frontend invalidates budget queries after imports, account preferences, tag changes and transfer decisions.

Review follow-up: history uses one SQL aggregation across all requested dates, grouped by UTC day and currency. Each period includes excludedCurrencies (currency and transaction count) for otherwise matching spending, without mixing currencies or converting money. Budget and pay-cycle boundaries use the same AnchoredPeriods implementation, included identically in both sibling branches so neither feature depends on the other.

## Category picker and preview

The editor searches existing categories and optional tags. Search only narrows the displayed options; selected chips remain visible and removable. Empty selections never mean all spending: that is a separate explicit choice. Missing category/tag/account selections have removal and recovery feedback. No categories are invented for uncategorised imports; the picker can adopt the category-model source when #29 lands.

Complete the budget details to see a preview before saving. Its date selects a period using the chosen frequency and anchor. Changing the definition, scope or date replaces the preview query and cancels obsolete requests; saving is disabled until the current preview succeeds. Empty matches are valid but explained. A failed preview offers retry and cannot be mistaken for a zero total. Categories and tags use OR matching; multiple matches still count a transaction once.

See `docs/issue-30/README.md` for mobile verification and the development-only synthetic fixture. The fixture is outside the production entry point and never calls the API.
