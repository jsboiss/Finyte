# Rules that are not obvious from the code

Behavioural rules only. Design rationale and verification belong on the pull request that made the change. Edit a line here only when the rule it states changes.

- **Accounts.** Provider fields (`Name`, `ProductName`, categories, numbers) are source data and never edited; user choices live in `CustomName`, `AccountTypeOverride` and `IncludeInAnalyticsOverride`. Type defaults exclude loans, investments and term deposits from combined spending; explicit overrides always win. Total balance includes every account regardless.
- **Balances.** Fiskil owns balances for connected accounts. OFX imports update a balance only for unconnected accounts, and only with a newer dated snapshot.
- **Posted status.** Null, empty, `posted` and `POSTED` all count as posted. Pending and undated rows never count in totals.
- **Internal transfers.** A row is a transfer when its description or reference names another household account by number; each row is decided on its own. `manual` and `excluded` marks are never overridden by detection. Transfers are left out of income and spending but not balances.
- **Tags.** Merchant rules match complete leading words of the merchant name (description only when there is no merchant). Manual tags survive rule changes; automatic ones are retracted only when no rule supports them. A "Transfers" tag is categorisation, not a transfer decision.
- **Budgets and pay cycles.** Anchors extend backwards as well as forwards. Monthly periods clamp with `AddMonths`: an April 30 anchor gives May 30. One currency per budget or profile; nothing is converted.
- **Recurring payments.** A month-end anchor (29th or later and last day of its month) repeats at month end; any other anchor returns to its day. Occurrences exist only on or after the anchor. Nothing is ever confirmed without a person; scores are review priority, not decisions.
- **Projections.** Cached read models exist only for derived dashboard data. Lists of source records are queried directly with paging. Any change that alters totals bumps `Tenant.FinancialDataVersion` so cached overviews rebuild.
- **Search.** Substring matches are literal (`%`, `_` and `\` are ordinary characters). Amounts are native; no exchange rates.

Running the app locally: [dev-testing.md](dev-testing.md).
