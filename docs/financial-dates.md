# Financial dates

Transactions store the instant the bank reported (`PostedAt`, UTC `timestamptz`). Every financial **date** is that instant read in the household's time zone. No label needs a "(UTC)" qualifier because nothing user-facing is a UTC day.

## Policy

- Each household has one time zone, `Tenant.TimeZoneId` (IANA, default `Australia/Sydney`). The owner changes it under Settings → Household time zone. `PUT /api/family/settings` validates it with `TimeZoneInfo.TryFindSystemTimeZoneById`.
- `FinancialCalendar` (`src/Finyte.Core/Scheduling`) is the only place instants become dates. `TenantCalendars` (scoped) resolves one per tenant. `Today`, `ToDate`, `StartOf`, `EndExclusive` and `NoonOf` replace every earlier `DateOnly.FromDateTime(x.UtcDateTime)` and `new DateTimeOffset(date, TimeSpan.Zero)`.
- Date-range queries compare `PostedAt` against local-midnight instants (`StartOf(from)` ≤ `PostedAt` < `EndExclusive(to)`), so the existing `PostedAt` indexes still serve them.
- Per-day grouping (overview daily cash flow, cash-flow chart, budget period history) uses `TimeZoneInfo.ConvertTimeBySystemTimeZoneId(PostedAt, zone).Date`, which Npgsql translates to `AT TIME ZONE`. `CalendarTranslationTests` pins that translation.
- Anchors, occurrence dates and period boundaries were already `DateOnly` and are unchanged.

## How a posted instant is chosen at ingestion

| Source | Rule |
| --- | --- |
| Fiskil | Parsed with the invariant culture and `AssumeUniversal`, then normalised to UTC. A `+10:00` value used to fail the `timestamptz` write. |
| OFX import | `DTPOSTED` carries a day, not a time. It is stored at local **noon** in the household zone, so the day survives a later zone change of up to ±12 hours. Rows imported before this change sit at UTC midnight (10 or 11 am in Sydney) and keep their day for any Australian zone. |
| No `PostedAt` | The row stays undated and unposted. Transaction search falls back to `CreatedAt` for date filters, as before. |

## What "today" means

`FinancialCalendar.Today` is the current day in the household zone. Budgets, pay cycles, recurring windows, the overview month key and projection staleness all use it. Before, a Sydney household's day rolled over at 10:00 or 11:00 local time.

The browser uses its own local day for default form values (`app/shared/calendar.ts`); rendered dates come from the API's `postedDate`, `from` and `to` strings. `toISOString().slice(0, 10)` is gone from the client, since it produced the previous UTC day during an Australian evening.

## Changing the zone

Changing the household zone rewrites no data. It bumps `FinancialDataVersion` and marks every overview projection pending, so totals recalculate on the next request. Historical totals can shift by the transactions that posted within the offset difference of a day boundary. Saved anchors do not move.

## Boundary cases considered

- **Local vs UTC midnight.** A 9 am AEST purchase on the 1st used to count in the prior month. It now lands on the 1st.
- **Daylight saving.** `StartOf` uses the zone's offset for that specific local midnight, and steps forward an hour if a zone's DST gap ever covers midnight (Sydney's does not).
- **Month-end clamping and leap years.** Unchanged, still covered by `PayCycleTests`, `BudgetApiTests` and `RecurringCalendarTests`. The two calendars differ deliberately: `AnchoredPeriods` (budgets, pay cycles) uses `AddMonths` clamping, so an April 30 anchor gives May 30; `RecurringCalendar` treats a day of 29 or more that is the last of its month as "month end", so April 30 gives May 31, while February 28 in a non-leap year stays on the 28th.
- **Inclusive display boundaries.** `from`/`to` in responses stay inclusive; queries use an exclusive next-day instant.
- **Multiple members.** Everyone in a household sees the same days because the zone belongs to the household, not the viewer.

## Migration `AddTenantTimeZone`

Adds `tenants.TimeZoneId` (default `Australia/Sydney`) and bumps every tenant's `FinancialDataVersion`, since month buckets move from UTC days to Sydney days and cached overviews must rebuild. No transaction rows change.

## Labels

| Was | Now | Why it is accurate |
| --- | --- | --- |
| Known payday (UTC) | A recent payday | Cycles repeat forwards and backwards from the anchor, so any payday works. |
| Known period start | A recent period start | Same schedule semantics as pay cycles. |
| Schedule anchor | First scheduled payment | Recurring occurrences exist only on or after the anchor. It is not "next due". |
| Recognise this field | Match future payments by | The alias field chooses how later payments are recognised. |
| Scheduled {date} · {status} | Next expected payment {date} / No payment found for {date} in imported data / Payment for {date} needs review | `nextDueDate` is the earliest outstanding occurrence, so it is only "next expected" while upcoming or due. |
| `dateBasis: "UTC"` | `dateBasis: "<household zone>"` | The pay-cycle response names the household calendar. |
