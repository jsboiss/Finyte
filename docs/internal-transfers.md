# Internal transfer review

Branch: `feature/internal-transfer-review`, based on `feature/import-data-source`.

Finance's prototype silently labels equal opposite payments on different accounts within three days as internal transfers. Finyte treats these as suggestions until a family member confirms the pair. No ordinary transaction tag controls transfer classification.

## User behaviour

- **Suggested pairs:** Both transaction descriptions, amounts, dates, and account names appear together. A suggestion never changes totals. Multiple possible counterparts are labelled; no greedy automatic pairing occurs.
- **Confirm transfer:** Links the two transactions and excludes both from spending, income, average daily spending, cash-flow charts, and spending by tag. This also applies when viewing one account. Original transactions, tags, and account balances are unchanged.
- **Not a transfer:** Dismisses only that pair. The decision survives refreshes and imports. Other possible counterparts remain reviewable.
- **Undo confirmation / Return to suggestions:** Removes the decision. The transactions count normally again and are eligible for detection.
- **Changed transactions:** A confirmed pair whose amount, account, currency, posting timestamp, or posted status no longer agrees with the reviewed snapshot is immediately ineligible for exclusion. The user can inspect and reconfirm it, or undo the link. This view covers all dates; the other views filter by the money-out transaction's date.

Transfers remain visible in the transaction list with a link to the confirmed-transfers view. Dashboard copy explains the effect on totals. Existing asynchronous dashboard projections refresh after decisions; cash-flow queries use the decision immediately.

## Detection and scope

Suggestions require two posted transactions in the same family, on different accounts, with equal opposite amounts in the same currency and UTC posting dates no more than three calendar days apart. The default money-out date range is the last 90 days; users can choose up to 366 days at a time. Candidate counterparts can fall three days outside that window. Results are paginated, 50 pairs per page.

Fee differences, FX conversions, split transfers, and transfers for which only one side is available are deliberately not matched. Equal amounts and dates do not establish that a transfer occurred: refunds and unrelated payments require user judgement. No external bank action is performed.

Suggestions are calculated from current transactions when requested, so OFX imports and provider syncs share the same detection behaviour. A confirmed transaction is reserved until its decision is undone, including stale confirmations awaiting review.

## Backend

- `GET /api/internal-transfers?status=suggested&from=2026-08-01&to=2026-08-31&page=1` lists review pairs. Other statuses are `confirmed`, `dismissed`, and `needs-review`.
- `POST /api/internal-transfers/review` takes transaction IDs, action (`confirm`, `dismiss`, `reset`), and the displayed amount/currency/accounts/posting timestamps. Confirm/dismiss reject stale transaction details with HTTP 409.
- Endpoints resolve the family from authentication and retain the existing subscription gate. Decisions record the last reviewer and timestamp.
- `internal_transfers` stores the reviewed pair and its snapshot. Foreign keys cascade when underlying transactions are deleted. Partial unique indexes prevent a debit or credit from joining two confirmed pairs. A PostgreSQL family-row lock serializes competing review decisions; the decision and projection invalidation commit together.
- A shared validity query drives analytics exclusion and transaction badges. Provider corrections cannot leave a stale pair classified indefinitely; existing provider-sync projection invalidation triggers dashboard refreshes.
- `GET /api/cash-flow` excludes valid confirmed pairs by default. `includeInternalTransfers=true` returns actual account movements for consumers that need them.

Apply the `AddInternalTransferReview` EF migration before using the feature. Keep the existing Temporal dashboard worker running to refresh cached dashboard totals.

## Review follow-up

Saved decisions are filtered, counted and paged in SQL; suggestions fetch only decision keys relevant to the candidate window. Stale confirmations remain visible across all dates. Dismissals, empty resets and repeated confirmations do not invalidate projections when the valid exclusion set is unchanged. Actual exclusion changes retain conservative family-wide invalidation. Narrowing those rebuilds to affected accounts and months is a separate optimization, not required to eliminate work caused by dismissals.

Posted-status comparisons accept mixed casing in both database and in-memory paths, including existing provider records. The date/account predicates remain unchanged. No status backfill or provider-contract change is required. PostgreSQL regressions cover mixed casing, SQL pagination and out-of-range stale confirmations. The earlier ancestry/rebase comment was already addressed by the content-preserving merge from main.
