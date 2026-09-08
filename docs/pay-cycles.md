# Pay-cycle breakdowns

Pay cycles use a family's saved payday schedule and explicit account selection. Finance's prototype inferred a recent payday from a similarly sized deposit and assumed fortnightly pay. Finyte instead keeps the schedule independent of bank credits, supports weekly, fortnightly and monthly cycles, and classifies savings movements only from valid, confirmed transfer decisions.

## Periods and scope

The anchor is a known payday in UTC. Weekly and fortnightly cycles repeat every 7 or 14 days in both directions from it. Monthly cycles use its day of month, clamped in shorter months without carrying the clamp forward: a January 31 anchor gives February 28 (29 in leap years), then March 31. No weekend or holiday adjustment is applied. The period starts on payday and ends immediately before the next payday.

The supported anchor and lookup dates are 1900-01-01 through 9998-12-31. Responses give inclusive `from` and `to`, plus dates to request the previous/next cycle. Current cycles observe through today UTC, completed cycles through their end date. Future cycles return no actual transactions or totals, even if future-dated rows exist. Expected income remains a full-cycle comparison. It is never included in actual totals.

Each profile has one currency and 1–100 explicit tracked account IDs, plus up to 100 disjoint savings destination IDs. All selections must belong to the family and have the profile currency when saved. The UI initially selects accounts included in analytics; the saved selection is independent of later account classification changes. Selected accounts excluded from the dashboard still count here. Names remain current. Unavailable saved IDs are reported rather than widening the scope. Account IDs are stored as PostgreSQL UUID arrays; there is no implicit account deletion cascade that changes profile scope.

## Actual activity and reconciliation

Only rows with a posted timestamp, the profile currency, and status null, empty, `posted` or `POSTED` count. Missing dates are never replaced with ingestion dates. Unposted rows and other currencies in the observed dates are counted separately; all undated rows in the tracked accounts are reported because they cannot be assigned to a cycle. No currency conversion is performed.

Every counted transaction belongs to exactly one audit bucket:

- `external-credit`: positive rows without a valid confirmed transfer; this includes income, refunds and other credits, not verified salary.
- `spending`: negative rows without a valid confirmed transfer. Credits do not automatically offset spending.
- `savings-out` / `savings-in`: confirmed transfers between tracked accounts and saved savings destinations.
- `transfer-out` / `transfer-in`: confirmed transfers to/from other accounts outside the tracked scope.
- `within-scope`: a confirmed transfer leg whose counterpart account is also tracked.
- `zero`: zero-value rows without a confirmed transfer.

Transfer decisions use the existing shared validity query. Suggested, dismissed or stale pairs do not hide ordinary activity. Each leg counts on its own posted date, so different settlement dates can leave a nonzero within-scope movement in a cycle. A counterpart in the next cycle or after today's cutoff never cancels a current debit. Savings out and savings returns are shown separately; net savings is out minus returns, not a savings-account balance.

`netMovement` is the signed sum of all counted rows: external credits minus spending, savings out and other transfers out, plus savings returns, other transfers in and signed within-scope movement. It describes changes from recorded activity, not available funds or an account balance. `expectedIncomeDifference` is external credits minus optional expected income; it does not claim those credits are salary. Category totals cover only spending, preferring the ledger's secondary category, then primary, then Uncategorised.

Summary queries aggregate the whole cycle before paginating the audit rows. Kind filters affect the audit count/page only. Stable posted-date/ID ordering makes pagination deterministic for an unchanged ledger. PostgreSQL reads use a repeatable-read snapshot so totals and the audit page in a response remain consistent during provider ingestion; separate page requests can reflect new ledger activity. There is no projection cache: imports, transfer decisions, provider corrections and preference changes appear on the next request.

## API

All endpoints require an authenticated family member and active billing access. Tenant identity comes only from the existing tenant resolver.

- `GET /api/pay-cycles`: saved profiles.
- `POST /api/pay-cycles`: create a profile, version 0.
- `PUT /api/pay-cycles/{id}`: replace preferences with `expectedVersion`.
- `DELETE /api/pay-cycles/{id}?expectedVersion=N`: delete only the profile, leaving transactions untouched.
- `GET /api/pay-cycles/{id}/breakdown?date=2026-09-08&page=1&pageSize=25&kind=spending`: metadata, scope, whole-cycle totals, categories, exclusions, and an audit page. Omit `date` for the current cycle and `kind` for all rows. Page size is 1–100.

Create/update body:

```json
{
  "name": "Household payday",
  "frequency": "fortnightly",
  "anchorDate": "2026-09-01",
  "currency": "AUD",
  "expectedIncome": 2500.00,
  "accountIds": ["00000000-0000-0000-0000-000000000001"],
  "savingsAccountIds": ["00000000-0000-0000-0000-000000000002"],
  "expectedVersion": 0
}
```

Expected income is nullable and nonnegative, in major units with at most two decimals, matching transaction storage. Savings destinations are optional. Malformed query/body values, invalid ranges and foreign or mixed-currency account selections return 400. Cross-family profile IDs return 404. Stale edits/deletes return 409, including concurrent database writes. Schedule edits recompute historical views; profile revisions are not historical snapshots.

Migration `AddPayCycleProfiles` adds only the new profile table. The account classification/preference branch is the parent so account names and deliberate selection work consistently. Automated tests exercise calendar edges, scope/isolation/billing, stable paging, expected-vs-recorded amounts, savings returns, transfer corrections, cross-cycle settlement and future-period behavior.

For the PostgreSQL migration/query/concurrent-edit regression, set `FINYTE_TEST_POSTGRES` to a disposable PostgreSQL database connection and run `dotnet test tests/Finyte.IntegrationTests`. That test creates and drops its own random `paycycle_test_*` schema, without modifying existing schemas. Without the variable, only this relational test is skipped.
