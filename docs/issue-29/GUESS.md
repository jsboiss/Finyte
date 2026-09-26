# Issue 29 — stage 2, guessed categories for uploads

Base: `claude/issue-29-category-override` (PR #71). Branch: `claude/issue-29-category-guess`.

Delivers the second half of Delan's decision: uploaded statements arrive with no category, so Finyte should
guess one, and a person's own choice still wins.

## What changed for someone using the app

A merchant rule can now carry a category as well as a tag. When a transaction arrives with no category of its
own — which is every transaction from an uploaded statement — the most specific matching rule supplies one. So
"anything from Coles is Groceries" fills in the gap that used to read Uncategorised.

## Order of precedence

1. A category the person set on that transaction.
2. The category the bank feed supplied.
3. A category from a matching merchant rule.
4. Uncategorised.

A guess only ever fills a gap. It never displaces what the bank sent, because Delan's instruction was to
default to the category that already arrives. If a rule should instead beat the bank's value, that is a
one-line change to the order and worth asking about — a rule is arguably a more deliberate statement of intent
than a feed's guess, and the current order means a rule silently does nothing on connected accounts.

Rules are already sorted longest key first, so the most specific matching rule wins. A rule whose tag has been
excluded on a transaction supplies no category there either, on the grounds that the person has already said
that rule does not apply to that row.

## Automated checks

- `dotnet build Finyte.slnx`: succeeded, 0 warnings.
- `dotnet test Finyte.slnx`: **292 passed, 7 skipped, 0 failed.** The 7 skips need `FINYTE_TEST_POSTGRES`.
- `npx tsc -b`, `npm run lint`, `node --test`: pass.

Three new tests, all passing first run:

| Case | Asserts |
| --- | --- |
| A rule categorises an uploaded transaction | one that arrived with no category reads Transport after the rule exists |
| A guess never displaces the bank's category | a transaction that arrived as Department stores stays that way |
| An override beats a guess | setting Home wins; clearing it falls back to the rule's Transport, not to blank |

The third is the one worth having: clearing an override could plausibly have reset to Uncategorised.

## Browser checks

None — the app needs a backend and database. No screenshots committed; images belong on the pull request.

## Not covered

- The dashboard still breaks spending down by tag rather than category. #29 asks for ranked category bars as
  the default breakdown; that remains open.
- Existing transactions are recategorised when rules change, through the same reconcile path that maintains
  automatic tags, so no backfill migration is needed. That path has not been exercised against a large tenant.
