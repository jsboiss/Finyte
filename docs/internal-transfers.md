# Internal transfers

An internal transfer is money moving between two of the household's own accounts. Each transaction is classified **on its own row** from what its bank description says. Nothing is inferred from other transactions, amounts or dates, and there is no pairing table.

## Rule

`InternalTransferDetector.Detect` (`src/Finyte.Core/Transfers`) reads every run of four or more digits in the description and reference. If exactly one *other* household account's `AccountNumber` ends with one of those runs, the transaction is a transfer to or from that account. It is not a transfer when:

- no run matches (`Transfer to savings`, or the other account is not in Finyte);
- more than one account matches (two accounts sharing the same trailing digits);
- the digits are the transaction's own card number (`Woolworths Card xx6486` is a purchase, and a run directly after the word "card" is ignored);
- the match is on an account in another household.

Only the trailing digits are compared, so masked forms (`xx6486`), full numbers and BSB + number all work. Accounts need an `AccountNumber` to be matched: Fiskil supplies it; an OFX import captures `<ACCTID>` from `<BANKACCTFROM>`/`<CCACCTFROM>` for an imported account that does not have one yet.

## Storage

`transactions.InternalTransferAccountId` (FK to `accounts`, set null on delete) names the other account. `InternalTransferSource` records why:

| Source | Meaning |
| --- | --- |
| `detected` | Set by the detector. Re-evaluated whenever the row or the account list changes. |
| `manual` | Marked by a family member with an explicit other account. Detection never overrides it. |
| `excluded` | Marked "not a transfer". `InternalTransferAccountId` is null and detection never overrides it. |
| null | Not a transfer as far as the detector can tell. |

Analytics exclude a transaction when `InternalTransferAccountId` is not null (`ExcludeInternalTransfers`). Pay cycles use the account it points at to decide `within-scope`, `savings-out`/`savings-in` and `transfer-out`/`transfer-in`, one row at a time. The two legs of a real transfer are classified independently; if only one bank labels its side, only that side drops out of the totals.

## When detection runs

- Fiskil transaction sync applies the detector to every inserted or changed row.
- Fiskil account sync reclassifies the whole household when an account is added or changed, so history picks up a newly connected account.
- OFX import applies the detector to new rows and reclassifies the household when it learns an account number.
- `POST /api/internal-transfers/reclassify` re-runs detection on demand (the "Re-run detection" button). Rows marked `manual` or `excluded` are untouched.

Any change to `InternalTransferAccountId` bumps `FinancialDataVersion` and marks overview projections pending.

## API

- `GET /api/internal-transfers?view=transfers|excluded&from=&to=&page=`: transactions with a counterparty (or those marked excluded), 50 per page, newest first.
- `POST /api/internal-transfers/review` with `{ transactionId, action, counterpartyAccountId }`: `mark` (requires another household account), `exclude`, or `reset` (clears the decision and re-runs detection for that row).
- `POST /api/internal-transfers/reclassify`: returns `{ changed }`.
- Transaction rows carry `isInternalTransfer`, `internalTransferAccountId`, `internalTransferAccountName` and `internalTransferSource`; `internalTransfers=include|exclude|only` filters on them.

All routes require an authenticated family member with active billing access.

## Migration `ClassifyInternalTransfersPerTransaction`

Adds the two columns, converts every `confirmed` pair from `internal_transfers` into `manual` marks on both legs, bumps `FinancialDataVersion`, then drops `internal_transfers`. Suggested, dismissed and needs-review pairs were never excluding anything and are not carried over. Existing rows are not detected until the next sync or import, or a click on "Re-run detection".

## Why not pairing

The previous implementation matched equal-and-opposite amounts on different accounts within three days, then needed a review queue for ambiguous pairs, stale confirmations and provider corrections. It could not tell two same-amount transfers apart and made a row's meaning depend on whether another account had synced. Reading the counterparty from the description is deterministic, per row, index-friendly and explainable to the user ("this went to your Savings account, xx6486").
