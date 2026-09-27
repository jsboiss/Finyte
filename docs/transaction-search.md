# Transaction search

The ledger searches the family's full transaction history before counting, sorting and paging. It includes every account by default, including accounts whose transactions may be excluded from analytics. Confirmed transfers remain visible with their existing badge.

This uses Finance's search as a prototype, with explicit tag matching, signed amount ranges, deterministic ordering, filtered totals and input validation. It does not inherit Finance's implicit account-type exclusions.

## API

`GET /api/transactions` keeps the existing authenticated family scope and subscription requirement. The response shape remains `{ items, page, pageSize, totalCount }`; `totalCount` is the count after all filters. Combine different filter fields with AND.

| Parameter | Meaning |
| --- | --- |
| `page`, `pageSize` | One-based page, default 1; size 1–250, default 25. Out-of-range pages return empty items with the filtered total. Invalid or overflowing offsets return 400. |
| `accountId` | Exact account ID. Omit for all family accounts. Unknown or foreign IDs produce no results. |
| `from`, `to` | Inclusive `yyyy-MM-dd` calendar dates. Uses `PostedDate`, falling back to the calendar day of `CreatedAt` for unposted rows, consistently with the displayed date. The upper bound includes the whole final day. |
| `search` | Literal, case-insensitive substring in description, merchant name or reference. Trimmed; maximum 200 characters. `%`, `_` and backslashes are ordinary characters, not wildcard syntax. |
| `category` | Case-insensitive substring in either provider category; maximum 200 characters. |
| `tagIds` | Repeat the parameter for multiple exact tag IDs, at most 50. Duplicate IDs are ignored. Names are not identifiers. |
| `tagMatch` | `any` (default) requires at least one selected tag; `all` requires every selected tag. Family boundaries apply to tag matching as well as transactions. |
| `untagged` | `true` selects transactions without family tags. Cannot be combined with `tagIds`. |
| `minAmount`, `maxAmount` | Inclusive signed decimal amounts in currency major units. For expenses between $20 and $100, use `minAmount=-100&maxAmount=-20`. Zero is a valid bound. |
| `currency` | Optional three-letter code, normalized to uppercase. Amount filtering and sorting compare native amounts; no exchange-rate conversion occurs. |
| `sort` | `-date` (default), `date`, `amount`, `-amount`, `description` or `-description`. Minus means descending. Amount order is signed, not absolute. |

Example: `/api/transactions?from=2026-06-01&to=2026-06-30&search=coffee&minAmount=-100&maxAmount=-1&currency=AUD&sort=amount&pageSize=25`.

Invalid ranges, sort modes and pagination return HTTP 400 validation problems. Malformed typed query values also return 400 in development and production. Search never changes transaction or analytics data.

Every sort has effective date, creation timestamp and unique transaction ID tie breakers. Offset pagination remains deterministic for an unchanged dataset; concurrent imports or edits can change the set between page requests. Filters, ordering, count and paging execute in the database. No migration is required.

## Placeholder UI

The filter form supports account, dates, search, category, exact multi-tag selection, any/all matching, untagged-only, signed amount bounds, currency and sort. Apply filters resets to page 1 in the same state update. Clear resets the applied search and form.

The query cache key includes the entire applied search and page. Changing searches does not display rows from the previous search, requests can be cancelled, and tag mutations invalidate all transaction searches so tag-filter membership and counts are recalculated. Merchant tagging is evaluated by the backend rather than predicted from the visible page. Request failures are displayed with a retry action.

## Verification

`TransactionSearchApiTests` covers filtering before pagination, date boundaries and missing posted dates, literal search, combined tags, tenant isolation, signed amounts and zero, every sort across tied pages, invalid inputs and the maximum calendar date. It also verifies that the full LINQ query translates with the PostgreSQL provider.

To exercise the complete endpoint projection against PostgreSQL, set `FINYTE_TEST_POSTGRES` to a test database connection string, then run `dotnet test tests/Finyte.IntegrationTests/Finyte.IntegrationTests.csproj --filter FullyQualifiedName~PostgreSqlEndpoint`. The database role must be able to create schemas. The test applies migrations in a uniquely generated schema and deletes only that schema afterward. Without the environment variable this test is explicitly skipped; the normal tests still use isolated in-memory databases. This catches provider-specific projection failures that in-memory tests and query-only translation checks cannot detect.

The complete backend suite, frontend build and lint should pass before merging.

Review follow-up: match-all tags use one count subquery over the unique transaction/tag assignments, irrespective of selected tag count. PostgreSQL integration tests cover filtering before paging and literal percent, underscore and backslash searches. Case-insensitive substring search and description ordering are retained; replacing them with unescaped ILIKE or case-sensitive ordering would change behavior. A trigram/expression index remains a workload-driven optimization; neither plain ILIKE nor a plain btree removes leading-wildcard scanning.

Dashboard drill-downs use URL-backed filters: postedOnly=true, analyticsOnly=true for combined accounts, direction=debit/credit/all, and internalTransfers=include/exclude/only. These apply before counting and pagination. Explicit account selection permits direct inspection of an analytics-excluded account. Tag links carry tag IDs (or untagged=true), the exact month/range, and the selected transfer treatment. Spend-by-tag allocations split multi-tag transactions; the ledger displays their full amounts. Balance cards link to account balances rather than claiming balances are a transaction aggregate.
