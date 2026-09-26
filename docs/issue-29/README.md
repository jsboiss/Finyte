# Issue 29 verification — stage 1, category override

Base: remote main `923baddb3f371e9786ce5b3bd5b13ff6c81f7065`, with the test fix from #68 merged in so the suite
is green. Branch: `claude/issue-29-category-override`.

Delivers the override half of Delan's 25 September decision: default to the category the ledger already
carries, and let the user correct it per transaction. The guessing half, for uploaded statements that arrive
with no category, is not in this branch.

## What changed for someone using the app

A transaction's category is now editable. Correcting one keeps the bank's original value untouched underneath,
marks the row as edited, and moves that transaction's spending into the corrected category everywhere —
budgets and pay-cycle breakdowns included.

## Resolution order

Override, then the ledger's secondary category, then its primary, then `Uncategorised`. That matches the
precedence the transactions list and pay cycles already used; only the override is new. It lives in one place,
`TransactionCategories`, so a screen cannot disagree with another about what a transaction's category is.

## Automated checks

All run, not read.

- `dotnet build Finyte.slnx`: succeeded, 0 warnings.
- `dotnet test Finyte.slnx`: **289 passed, 7 skipped, 0 failed**. The 7 skips are the PostgreSQL cases, which
  need `FINYTE_TEST_POSTGRES`.
- `npx tsc -b`, `npm run lint`, `node --test`: all pass.

Five new tests in `TransactionCategoryOverrideTests`:

| Case | Asserts |
| --- | --- |
| Override replaces, clearing restores | the stored `PrimaryCategory` is never modified |
| A corrected transaction moves between budgets | Kmart at 45.00 leaves a Department stores budget and lands in a Transport one |
| Blank and whitespace overrides | rejected with 400, category unchanged |
| Another family's transaction | 404, category unchanged |

The budget case is the one that matters: without it, a correction would look right on screen and leave reports
wrong.

## A correction I made to my own test

My first version asserted budget spend of `4500`, assuming minor units. The endpoint reports major units, so
the real figure is `45`. The test was wrong, not the code. Recording it because the failure looked at first
like a bug in the override.

## Browser checks

None — the app needs a running backend and database. Screenshots are attached to the pull request rather than
committed here. What was captured: the real `CategoryEditor` through a temporary Vite harness with the
production stylesheets at 402px, showing a category before a correction and after one, with the edited marker.

The inline edit form, the datalist of existing categories and the "Use imported" reset are not captured.

## Not covered

- **Guessing for uploaded statements.** They arrive with no category at all, so they stay `Uncategorised`
  until someone sets one. Delan has asked for guessing; it needs its own change, most likely letting a
  merchant rule carry a category so "anything from Coles is Groceries" fills one in.
- **The dashboard breakdown.** Spend by tag still splits by tag, not category. #29 wants ranked category bars
  as the default breakdown; that is a separate piece.
- Category suggestions in the editor reuse `/api/budgets/categories`, which sits behind the billing gate. Without a
  subscription the list is simply empty and free text still works.
