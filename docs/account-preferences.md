# Account classification and preferences

Finance's account editor was the prototype. Finyte keeps user choices separate from provider facts and makes analytics inclusion independent of classification.

## Behavior

- `Name`, `ProductName`, and `ProductCategory` remain source data. Nullable `CustomName` supplies the display name across account lists, transactions, transfer review and overview labels. Clearing it restores the current source name.
- `AccountTypeOverride` overrides a conservative category default. Supported values are `everyday`, `savings`, `credit-card`, `home-loan`, `offset`, `loan`, `investment`, `term-deposit`, and `other`. Clearing it restores inference. Unknown categories are `other` and remain included in analytics.
- Only exact category values documented in the local Fiskil accounts reference are mapped. `TRANS_AND_SAVINGS_ACCOUNTS` defaults to everyday; it does not distinguish savings or offset accounts. Names and product names are never guessed from free text.
- Type defaults exclude home loans, other loans, investments and term deposits from combined spending/income. Everyday, savings, credit cards, offsets and other/unclassified accounts are included. Savings and offsets are included because they can carry ordinary spending; transfers are handled separately by confirmed transfer decisions.
- `IncludeInAnalyticsOverride` is nullable: null uses the current type default, true always includes, false always excludes. Account type changes therefore affect inclusion only when using the default. Explicit choices survive bank syncs.
- The preference applies only to **combined income and spending**: overview totals, daily cash flow and spend by tag. Total account balance still includes every account. Selecting an individual account shows its income/spending regardless of this setting, with existing confirmed-transfer exclusion still applied. The transaction list and transfer review remain unrestricted by analytics preferences.
- Fiskil owns balances for connected accounts, including disconnected accounts retaining a Fiskil identity. OFX imports update balances only for unconnected accounts, using a newer ledger snapshot and its effective date. Missing balances display as unavailable; legacy creation-time placeholders are not reported balances.

## API

`GET /api/accounts` retains existing fields; `name` is now the effective display name. It additionally returns:

`originalName`, `customName`, `accountType`, `inferredAccountType`, `accountTypeOverride`, `defaultIncludeInAnalytics`, `includeInAnalyticsOverride`, `includeInAnalytics`, `isProviderManaged`, `productName`, `productCategory`, `balanceAsOf`, `preferencesVersion`, `manualBalanceVersion`.

`PUT /api/accounts/{accountId}/preferences` replaces the three user preferences:

```json
{
  "customName": "Family savings",
  "accountTypeOverride": "savings",
  "includeInAnalyticsOverride": null,
  "expectedVersion": 0
}
```

Use explicit nulls to reset preferences. Names are trimmed, blank names reset, and names over 120 characters or unsupported types return 400. `expectedVersion` must be the last `preferencesVersion`; stale saves return 409, preserving the current values. Bank sync does not increment the preferences version. All family members can set shared preferences, including without a paid subscription.

Manual balance editing is unavailable. `POST /api/accounts` accepts a name and currency and creates an account without a reported balance. OFX balance changes invalidate projections even if every transaction is a duplicate. Older snapshots and files without balances leave a known balance intact. Available balance is used only when dated consistently with the ledger balance.

## Validation

`dotnet test tests/Finyte.IntegrationTests/Finyte.IntegrationTests.csproj` covers source preservation under real sync-service upserts, reset-to-provider behavior, tenant boundaries, stale edits, combined versus direct analytics, balance preservation, projection invalidation, OFX snapshot ordering and provider balance protection. Existing transfer tests remain relevant to the unchanged transfer-exclusion pipeline.

Run `npm run build` and `npm run lint` in `src/Finyte.Web`. The migration is `AddAccountPreferences` and requires the preceding internal-transfer migration.

Review follow-up: spending currency selection now uses analytics-eligible accounts consistently for pending and rebuilt overviews and cash-flow. Balances deliberately include all accounts; this is already explained beside the dashboard totals. Multi-currency balance aggregation and FX conversion remain a separate limitation of the existing overview contract.
