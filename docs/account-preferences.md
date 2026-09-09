# Account classification and preferences

Finance's account editor was the prototype. Finyte keeps user choices separate from provider facts and makes analytics inclusion independent of classification.

## Behavior

- `Name`, `ProductName`, and `ProductCategory` remain source data. Nullable `CustomName` supplies the display name across account lists, transactions, transfer review and overview labels. Clearing it restores the current source name.
- `AccountTypeOverride` overrides a conservative category default. Supported values are `everyday`, `savings`, `credit-card`, `home-loan`, `offset`, `loan`, `investment`, `term-deposit`, and `other`. Clearing it restores inference. Unknown categories are `other` and remain included in analytics.
- Only exact category values documented in the local Fiskil accounts reference are mapped. `TRANS_AND_SAVINGS_ACCOUNTS` defaults to everyday; it does not distinguish savings or offset accounts. Names and product names are never guessed from free text.
- Type defaults exclude home loans, other loans, investments and term deposits from combined spending/income. Everyday, savings, credit cards, offsets and other/unclassified accounts are included. Savings and offsets are included because they can carry ordinary spending; transfers are handled separately by confirmed transfer decisions.
- `IncludeInAnalyticsOverride` is nullable: null uses the current type default, true always includes, false always excludes. Account type changes therefore affect inclusion only when using the default. Explicit choices survive bank syncs.
- The preference applies only to **combined income and spending**: overview totals, daily cash flow and spend by tag. Total account balance still includes every account. Selecting an individual account shows its income/spending regardless of this setting, with existing confirmed-transfer exclusion still applied. The transaction list and transfer review remain unrestricted by analytics preferences.
- Balances on manual accounts can be updated as snapshots. Updates never insert transactions or change currency. OFX imports do not update balances. Bank-connected accounts and disconnected accounts retaining a Fiskil account ID reject manual balance edits.

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

`PUT /api/accounts/{accountId}/balance` records a manual balance snapshot:

```json
{
  "currentBalance": 1234.56,
  "availableBalance": null,
  "expectedVersion": 0
}
```

Here `expectedVersion` is the last `manualBalanceVersion`. Current balance is required; null available balance clears it. Both values accept at most two decimal places and 16 whole digits; negative balances are permitted. The server sets `balanceAsOf` to the update time. An active subscription is required, consistent with manual account creation. The preferences endpoint cannot alter balance, currency or bank metadata.

Both endpoints return the updated account, 404 for another family's account, 400 for invalid input, and 409 for concurrent edits. EF concurrency tokens provide protection when requests race after their initial version checks. Preference/balance changes and projection invalidation are saved together. The migration invalidates existing projections so inferred defaults apply to existing accounts after deployment.

The placeholder `/accounts` page exposes current defaults, explicit overrides, reset controls, balance timestamps and conflict recovery. Dashboard scope notes explain exclusions and link to preferences.

## Validation

`dotnet test tests/Finyte.IntegrationTests/Finyte.IntegrationTests.csproj` covers source preservation under real sync-service upserts, reset-to-provider behavior, tenant boundaries, stale edits, combined versus direct analytics, balance preservation, projection invalidation, manual balance validation and provider balance protection. Existing transfer tests remain relevant to the unchanged transfer-exclusion pipeline.

Run `npm run build` and `npm run lint` in `src/Finyte.Web`. The migration is `AddAccountPreferences` and requires the preceding internal-transfer migration.

Review follow-up: spending currency selection now uses analytics-eligible accounts consistently for pending and rebuilt overviews and cash-flow. Manual balance decimal strings are covered by a real HTTP regression test. Balances deliberately include all accounts; this is already explained beside the dashboard totals. Multi-currency balance aggregation and FX conversion remain a separate limitation of the existing overview contract.
