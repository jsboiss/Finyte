# Issue 38 verification

Base: `claude/issue-63-currency-scope` (PR #66), which is itself based on remote main
`923baddb3f371e9786ce5b3bd5b13ff6c81f7065`. Stacked because #66 already changed the dashboard strings this
issue covers; basing on main instead would have conflicted on the same lines.

Branch: `claude/issue-38-money-in`.

## The decision this implements

Spending is **gross**, never net of refunds. Settled by Delan on 25 September 2026 and recorded on #38.

A refund is a credit. It appears as money in and never reduces a spending figure. The consequence worth
stating: a past period's spending does not change when a refund lands later, which is what keeps every total
traceable to transactions with a matching account, date, currency and transfer scope — the design constraint
in #48.

A net-of-refund mode is not blocked by this, but it would need refund matching, a cross-period policy and a
partial-refund policy, and would have to be an explicit opt-in rather than a change to these figures.

## Counting-rule audit

Checked every surface that presents a credit total, to confirm they already agree with the gross rule:

| Surface | Counting rule | Agreed already? |
| --- | --- | --- |
| Dashboard cash flow | credits summed, debits summed, neither offsets the other | yes |
| Budgets | `BudgetQueries` filters `x.Amount < 0`; credits never enter a budget | yes, and `docs/budgets.md` already said "Credits are not inferred to be refunds" |
| Pay cycles | exhaustive disjoint buckets; `external-credit` and `spending` are separate | yes, and `docs/pay-cycles.md` already said "Credits do not automatically offset spending" |
| Transaction result totals (#61) | money in and money out reported separately, never netted | yes |

No counting code changed. The rules were already gross; what was missing was saying so, and not calling every
credit income.

## Changes

- `CashFlowChart.tsx` — the remaining `Income` / `Expenses` labels, which #66 did not reach: the summary
  figures, the best-day label, the month-to-date aria-label and the legend.
- `PayCyclesPage.tsx` — the spending and credits help now state the gross rule in the place the two figures are
  compared.
- `docs/budgets.md`, `docs/pay-cycles.md` — the gross rule recorded as a decision with its rationale, so it
  reads as a position rather than an unfinished feature.

Deliberately unchanged: **"combined spending and income"** on the accounts, dashboard and recurring pages. That
names an account-scope preference, not a credit total, so it does not make the claim this issue is about.
Renaming it would touch three more surfaces for no gain. Flagging it in case you disagree.

`expectedIncome` on pay cycles is also untouched — it is a salary target the user typed in, so income is the
right word there, and the surface already says the credits it is compared against are not verified salary.

## Automated checks

- `npx tsc -b`: passed.
- `npm run lint`: passed.
- `node --test src/Finyte.Web/tests/*.mjs`: 5 passed.
- No C# changed in this branch, so nothing here needs a build. (#66 below it does.)

## Browser checks

None — the app cannot run here without a backend.

Screenshots are attached to the pull request rather than committed here. What was captured: the real
`CashFlowChart` in `summary` mode through a temporary Vite harness with the production stylesheets and fixture
points, at 620px rather than a phone viewport, for the panel overflow reason recorded in
`docs/issue-63/README.md`.

The first capture caught a real miss: `Money in` was still paired with `Expenses`, because the summary tile
label is separate from the legend. Fixed and recaptured. The remaining help text and the two docs are not
visually verified.

## Not covered

#38 stays open only if more is wanted; from its acceptance criteria this leaves nothing outstanding. Refund
allocation and a net-spending mode were explicitly decided against rather than deferred.
