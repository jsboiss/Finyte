# Recurring payment candidate ranking

Stage one ranks payments for an existing series and explains the evidence. Every confirmation, alias approval, price edit and lifecycle change remains explicit. GET requests write no decisions or review events. Initial discovery still uses the existing consecutive-occurrence detector; this change does not relax discovery gaps or automatically join completely unrelated billing names.

## Evidence and confidence

`GET /api/recurring-payments/{id}/transactions` adds `ranking` to each candidate: `version`, `score`, `confidence`, `matchKind`, `reasons`, `competingSeriesIds`, and `competingPaymentCount`. The existing `evidence` field contains the same explanations. Version `recurring-candidates-v1` identifies the deterministic rules. Scores are ordering weights, not measured probabilities.

- A distinctive, field-specific approved alias contributes 60 points. Unicode, case and punctuation normalization follows the existing alias rules.
- Distinctive token overlap with an approved alias contributes 35 points and suggests a possible name change. At least one token must overlap and cover at least half of the larger token set. Short tokens, purely numeric tokens, processor names and common billing/company words do not establish identity. Names are compared in the alias's chosen field; descriptions cannot satisfy merchant aliases.
- Exact generic aliases contribute only 10 points. An Apple/PayPal label alone cannot become a medium or high confidence suggestion.
- A posting within three calendar days of its anchored occurrence contributes 25 points.
- An equal expected amount adds 10 points. A different fixed amount within 25% of the estimate adds 5; other changed amounts add none. Variable amounts add no proximity points. An approved name can remain high confidence after a substantial price increase. No price estimate is updated.
- Outside the payment window, the total is capped at 40. Without distinctive name evidence, timing and amount remain low confidence.

High confidence starts at 80, medium at 55, and lower scores are low confidence. These labels describe review priority, not authorization to match. Completely changed names without distinctive overlap remain visible as weak evidence for manual linking.

## Competition and prior decisions

Candidates are compared before pagination. A candidate scoring at least 55 is ambiguous when another payment for the same occurrence, or another active series for the same account/currency, scores at least 55 and is no more than 15 points below it (or scores higher). Other series are tenant scoped and their names are included in the explanation. No winner is automatically assigned.

Rejected transaction/occurrence combinations, confirmed transaction reservations, occupied occurrences, inactive series and occurrences before the tracking anchor receive zero ranking weight and `review-only`. They remain available through the existing manual review controls. Resetting a rejection restores its eligibility; a rejection for another occurrence does not become a blanket merchant exclusion. Stale confirmations continue to reserve their slots until explicitly reviewed.

The ordering is high, medium, ambiguous, then low; within each group, score descending, posted timestamp descending, and transaction ID provide deterministic pagination for unchanged data. Ambiguity is based on the whole evidence range, never just the current page. Reads use the existing repeatable-read snapshot.

The service includes six days on either side of the visible range to cover complete competing payment windows. Selecting one occurrence uses its full three-day window even when visible dates are narrower. Evidence reads are capped at 10,000 eligible transactions including this context; requests exceeding the cap return 400 with instructions to narrow the range rather than silently discarding candidates. There are no new provider calls, external models or ranking tables.

## Integration and validation

This branch starts from `origin/main` at `66f1638`. Main did not contain recurring payments, so the implementation from `710025a` is included as working-file changes. API registration, frontend routes, database configuration and the migration model were integrated with main's budgets, pay cycles and automatic tagging. Ranking itself adds no migration; the prerequisite `AddRecurringPayments` migration is included.

Validation covers price changes, possible renames, generic processor names, field-specific aliases, competing series/payments, inactive and foreign series, date boundaries, rejection/reset behaviour, ranking before pagination, competition beyond the visible date range, and read-only suggestions. PostgreSQL integration tests exercise the actual migrations and candidate endpoint. A synthetic browser fixture verifies that the UI shows an approved-name match, a renamed/higher-priced payment, and an unrelated equal-price debit in that order with high/medium/low explanations.

Later automation should be opt-in and evaluated against reviewed suggestions before enabling any automatic confirmation. This stage does not collect an acceptance-rate dataset or claim calibrated accuracy.
