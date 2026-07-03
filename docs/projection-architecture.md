# Projection Architecture

This app uses projections for fast, derived UI data. Raw/domain tables remain the source of truth. Projection rows are disposable read-model/cache rows that can be rebuilt from raw data.

## When To Use A Projection

Use a projection when the UI needs a derived answer quickly and calculating it on every request would be expensive or slow.

Good projection candidates:

- dashboard totals and trend cards
- chart payloads
- tag/category spend breakdowns
- budget summaries
- subscription suggestions or detection outputs
- grouped or aggregated reporting data

Prefer raw queries when the page is primarily a list of source records.

Usually not a projection:

- paginated transaction history
- account transaction lists
- basic filtered/searchable source data
- user-owned domain records such as tags, tag rules, budgets, and subscriptions

Those should use indexed server-side queries, pagination, and normal domain tables.

## Projection Rules

Every projection needs four things:

1. A projection key.
2. A scope key.
3. A rebuild method.
4. An invalidation rule.

The projection key identifies the read model type, for example `overview`, `budget-summary`, or `subscription-suggestions`.

The scope key identifies the exact cached answer, for example:

```text
overview:account:{accountId}:range:{preset}
overview:all:range:{preset}
budget-summary:account:{accountId}:period:{month}
subscription-suggestions:tenant
spend-by-tag:account:{accountId}:range:{from}:{to}
```

Keep scope keys explicit and stable. If two UI requests can produce different answers, they should not share a scope key.

## Invalidation

A projection does not update itself automatically. Any feature that changes data used by a projection must call the projection invalidator.

Common invalidation examples:

- imported or new transaction: mark affected account and all-account overview scopes stale
- transaction tag changed: mark tag/category spend projections stale
- tag rule applied to many transactions: mark tenant projections stale
- budget edited: mark budget summary projections stale
- account balance changed: mark current balance/dashboard projections stale
- account deleted: clear or stale account-scoped projections

For small, obvious changes, prefer precise invalidation. For broad or uncertain changes, tenant-wide stale marking is acceptable.

Do not loop through hundreds of changed transactions and rebuild per transaction. Collapse the change into a broader invalidation, such as a date range, account set, or tenant-wide stale mark.

## Stale Handling

Projection payload rows should usually not be deleted during invalidation. Instead, mark their `projection_states` rows as stale.

The read path should:

1. Check whether the requested projection state is missing or stale.
2. Rebuild the requested scope if needed.
3. Return the rebuilt payload.
4. Clear the stale marker on successful rebuild.

This allows existing payloads to remain available while the system knows they need repair.

## Cleanup

Projection rows are disposable, but they still need a lifecycle.

Cleanup should happen separately from normal invalidation. Later, add scheduled or repair cleanup for:

- old projections that are unlikely to be read again
- projection states with stale or failed status beyond a retention period
- projections for deleted accounts
- projections for deleted tenants
- obsolete projection scopes after a projection design changes

Normal rebuilds should overwrite existing projection payloads. Hard deletion should be reserved for repair, cleanup, or deleted domain scope.

## Development Checklist

When adding a new metric or page, decide first:

- Is this a raw/source list? Use indexed queries and pagination.
- Is this an expensive derived answer? Add a projection.
- Is this user-owned data? Store it as a domain model, then invalidate projections that depend on it.

For a new projection:

- add a constant in `ProjectionKey`
- define a stable scope type/key
- implement a full rebuild from raw/domain tables
- route rebuilds through `IProjectionDispatcher`
- add invalidation methods through `IProjectionInvalidator`
- ensure the read endpoint checks missing/stale state before returning cached data
- add tests for rebuild success, rebuild failure, and invalidation fanout

Raw tables are truth. Projections are fast answers.
