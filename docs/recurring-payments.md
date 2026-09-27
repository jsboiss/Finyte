# Recurring payments and subscriptions

Existing-series review now includes [ranked candidates and confidence explanations](recurring-candidate-ranking.md). Read that document for ordering, ambiguity checks, evidence limits and the integration with current main.

Recurring payments separate a stable user-owned series from bank transaction descriptions. A series has its own GUID, display name, account, currency, cadence, first tracked due date, expected amount, fixed/variable amount mode, lifecycle state and version. Renamed merchants and price changes never create a new identity automatically. No provider API changes or background detection job are required.

## Discovery and calendar

Discovery examines dated, posted negative transactions in accounts currently included in analytics, excluding confirmed internal transfers, future dates, positive credits and transactions already reserved by a confirmed recurring decision. A manually created series may deliberately use any family account. Every profile and transaction query is tenant scoped; accounts and currencies cannot be supplied from another family.

At least three distinct nominal occurrences are required. Detection compares transactions with anchored weekly, fortnightly, monthly, quarterly or yearly schedules, allowing three calendar days either side of each nominal due date. It fits supported calendar schedules, rather than dividing an average gap into a cadence. Amount is evidence and a suggested estimate, not grouping identity. Field-aware merchant/description aliases are normalized conservatively; generic aliases do not automatically establish a merchant. Ambiguous overlaps remain suggestions requiring explicit selection, and disjoint schedules at the same merchant can become separate series.

All dates are calendar days in the household calendar (see [financial dates](financial-dates.md)). Weekly and fortnightly schedules repeat by 7/14 days. Monthly, quarterly and yearly occurrences are calculated independently from the original anchor. An anchor on the 29th, 30th or 31st that is the final day of its month repeats at month end; other anchors keep their day and clamp only in shorter months. For example January 30 gives February 28 and March 30, and February 28 in a non-leap year stays on the 28th. Weekend and holiday adjustments are not inferred. The anchor is the first tracked nominal occurrence; no unpaid predictions are invented before it. Discovery may use an earlier, phase-preserving anchor to describe an initial clipped February posting accurately.

Discovery is read only. Accepting a suggestion sends the explicitly selected transaction IDs, their nominal occurrence dates and displayed evidence fingerprints. A series may also start with no confirmed history. Approved aliases are included explicitly in the creation request. Dismissing a discovery is reversible and keyed by account, currency, alias field/value, cadence and phase; it is independent of price or the first visible history row. Disjoint runs with the same hypothesis share dismissal state.

## Review and evidence

Each transaction/series/occurrence combination can be confirmed, rejected or reset. Every mutation requires the current series version and every new confirmation requires a fingerprint of the displayed bank facts. A changed amount, date, account, currency, merchant, description, reference or status rejects a stale confirmation screen with 409.

Each review appends immutable snapshot evidence with reviewer and time. The current decision may change, but prior review rows are never rewritten. If provider facts change or disappear after confirmation, or an edited schedule no longer contains that occurrence, it becomes `needs-review` and contributes no paid amount. All-series needs-review counts scan current confirmations in bounded pages, including confirmations outside the selected display horizon.

One transaction can occupy only one confirmed series, and one series can have only one confirmed transaction for each scheduled occurrence. Even stale confirmations reserve their slots until explicitly reviewed. Reconfirming the same transaction against another occurrence of the same series releases the old slot, appends a `reassigned` event, and confirms the new date. Linking across series requires releasing the old decision first. This avoids duplicate paid totals when multiple series share a merchant alias.

Existing-series candidate pages deliberately expose all eligible account debits in the requested history, enabling manual review of name and price changes. Each row explains whether it matches an approved alias, falls inside the expected window, or differs in amount. Weak matches never confirm themselves. An optional occurrence date restricts the candidate query to that occurrence's window. Manual confirmation may deliberately link an eligible payment outside that window, but the target must be a scheduled occurrence on or after the anchor.

Approving a new alias is a separate opt-in field on a confirmation (`learnAliasField`). Removing an alias stops future name matching without unlinking past confirmed payments. Rejection persists for the chosen transaction and occurrence. Resetting a rejection returns that combination to review. Reject/reset remain possible when the underlying transaction is deleted, using the saved immutable evidence.

## Lifecycle, due state and costs

Only the user sets a series to active, paused or cancelled. Missing payments never imply cancellation. Confirmed historical payments remain visible in paused and cancelled series; pausing/cancelling suppresses active cost estimates and future due statuses. An account or currency change requires a separate series, preserving the original history.

Occurrence status is `paid`, `needs-review`, `upcoming`, `due`, `no-payment-found`, `paused` or `cancelled`. Upcoming means the three-day window has not opened; due means the payment window is open. After that window closes, no-payment-found means the app has no valid confirmed payment, not proof that a bill is unpaid. The next-due summary chooses the earliest outstanding occurrence in the explicit display horizon, including past dates. `none-in-range` means there is no outstanding occurrence in that range. It does not mean the series ended.

Cost summaries are separated by currency with no currency conversion. For active series, estimates annualize weekly ×52, fortnightly ×26, monthly ×12, quarterly ×4 and yearly ×1, then divide by 12 for an estimated monthly run rate. Variable series use the user's current estimate and are counted separately. These are planning estimates, not a sum of actual payments, exact future calendar charges or account balances. Editing the expected amount does not rewrite actual evidence.

## API and limits

All routes are under `/api/recurring-payments` and require an authenticated family member with active billing access.

- `GET /`: series summaries and per-currency cost estimates. Optional `from`/`to` define the due-status horizon; default today−1096 days through today+366 days.
- `GET /discovery?from=...&to=...&page=1&pageSize=20&dismissed=false`: a page of suggestions and full selected-run evidence. Defaults to the last 1096 days through today.
- `POST /discovery/decisions`: `{candidateKey, action:"dismiss"|"reset"}`.
- `POST /`: `{name,accountId,currency,cadence,anchorDate,expectedAmount,amountMode,aliases:[{field,value}],history:[{transactionId,occurrenceDate,fingerprint}]}`. Amounts are major units; expected amount is positive while evidence debits remain signed negative.
- `PUT /{id}`: `{name,cadence,anchorDate,expectedAmount,amountMode,state,expectedVersion}`.
- `GET /{id}/occurrences?from=...&to=...`: all nominal occurrences in the explicit range plus any stale confirmation dates requiring review. Defaults to today−31 through today+90 days.
- `GET /{id}/transactions?from=...&to=...&occurrenceDate=...&page=1&pageSize=25`: eligible transaction evidence and review signals.
- `GET /{id}/history?page=1&pageSize=25`: immutable review events, saved snapshot, current bank evidence when available, and current decision status.
- `POST /{id}/decisions`: `{transactionId,occurrenceDate,action:"confirm"|"reject"|"reset",expectedVersion,fingerprint,learnAliasField:null|"merchant"|"description"}`.
- `DELETE /{id}/aliases/{aliasId}?expectedVersion=N`: remove an alias. Past confirmations remain intact.

Dates must be between 1900-01-01 and 9998-12-31. Requested ranges span at most 1827 days. Discovery explicitly rejects ranges with more than 10,000 eligible rows and asks the caller to narrow the range; it never silently drops transactions. Candidate/history lists paginate, with deterministic timestamp/ID ordering and a page size of 1–100. Up to 300 selected history payments cover a five-year weekly run. Families can hold 200 series, each with at most 20 aliases. Invalid inputs return 400, foreign/missing series return 404, stale evidence/versions or conflicting reservations return 409.

PostgreSQL mutations lock the family before locking selected transaction rows and reading current evidence. Confirmation, slot release, alias approval, immutable review insertion and version update commit atomically. Conditional unique indexes provide a second layer of reservation protection. Reads use repeatable-read snapshots; separate page requests may see newly ingested data. Source transaction deletions do not cascade away recurring evidence.

Migration `AddRecurringPayments` adds five recurring-specific tables and leaves existing banking data unchanged. This branch starts from account classification/preferences; it does not depend on the sibling automatic-tagging, filtering or pay-cycle branches.

Run `dotnet test tests/Finyte.IntegrationTests` for calendar, discovery and API tests. Set `FINYTE_TEST_POSTGRES` to a test PostgreSQL connection to also exercise migration, provider corrections, alias learning, competing confirmations/edits and deleted-source recovery. The relational test creates and removes only its own generated schema.

## Validation

All 143 backend tests passed with PostgreSQL enabled. The migration was also applied to an existing account-preferences database containing synthetic banking history. EF reported no pending model changes. The frontend production build and lint passed; Vite retains its existing large-bundle advisory.

Desktop and 390px mobile browser checks covered discovery of a price-changing series, selection and confirmation of historical payments, a renamed and higher-priced payment, explicit alias approval, rejection of a nearby unrelated debit, changed-source review, deleted-source recovery, and retained audit evidence. Mobile content stayed within the viewport and no browser errors were logged. Test services used isolated synthetic data and were stopped after validation.
