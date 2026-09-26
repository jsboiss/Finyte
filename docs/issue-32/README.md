# Issue 32 verification

Branch: `issue-32-date-policy`, based on `origin/main` `923badd`. Policy and design are in [financial-dates.md](../financial-dates.md).

## Automated checks

- `dotnet test tests/Finyte.IntegrationTests`: 284 passed, 2 failed, 7 skipped. The two failures (`CombinedSpendingUsesPreferencesButBalanceAndDirectInspectionRemainAvailable`, `SuggestionsAreReadOnlyAndConfirmationExcludesBothLegsAcrossMonthAndAccountBoundaries`) assert `AccountBalanceMinorUnits` and fail identically on `origin/main`; they are unrelated to this change.
- New: `CalendarTranslationTests` pins the `AT TIME ZONE` SQL translation for daily grouping and the Sydney midnight/DST bounds.
- `npx tsc -b`, `npm run lint` and `node --test` (13 tests, 3 new for `shared/calendar.ts`) pass.
- `FINYTE_TEST_POSTGRES` opt-in tests were not run; the live-app checks below cover the migration and the translated queries against real PostgreSQL instead.

## Live app

Started with `scripts/dev.ps1 start` against the preserved `finyte_recurring_samples` database (280 transactions).

- API log on startup: `Applying migration '20260926003649_AddTenantTimeZone'`, then `Now listening`. `tenants.TimeZoneId` = `Australia/Sydney` and `__EFMigrationsHistory` lists the migration.
- `GET /api/cash-flow?from=2026-09-01&to=2026-09-03` returns per-day buckets from the `AT TIME ZONE` grouping (1 Sept: in 9573 / out 42383 minor units).
- **Zone switch proof.** `PUT /api/family/settings {"timeZoneId":"Pacific/Honolulu"}` moved that whole 1 Sept bucket to 31 Aug (the rows sit at `00:00Z`, which is 14:00 the previous day in Honolulu) and `postedDate` on the matching transactions became `2026-08-31`. Switching back to `Australia/Sydney` restored the original days. `FinancialDataVersion` went 12 → 14 across the two changes. `Mars/Olympus` returned 400.
- Settings page shows the new **Household time zone** panel ([settings-time-zone.png](settings-time-zone.png)).
- Pay cycles at 390 × 844: the editor is labelled **A recent payday** and defaults to the browser's local day ([mobile-payday.png](mobile-payday.png)). A temporary fortnightly profile anchored on 1 Sept 2026 produced the cycle **1 Sept 2026 – 14 Sept 2026 · Completed cycle**, "Recorded activity through 14 Sept 2026. 21 transactions counted in AUD. Dates follow the Australia/Sydney calendar", with each row showing its calendar day ([mobile-cycle.png](mobile-cycle.png)). The profile was deleted afterwards; no other data was changed.
- Recurring editor shows **First scheduled payment** and **Match future payments by**; the tracked series list reads "Payment for 4 Sept 2026 needs review" instead of a raw status ([mobile-recurring-editor.png](mobile-recurring-editor.png)).
- Budget editor shows **A recent period start** ([mobile-budget-editor.png](mobile-budget-editor.png)).

Screenshots stop above the transaction rows because the sample ledger contains real-looking payee names.
