# Issue 30 verification

Base: current remote main `210d846c4d0719886e4b08d17f45022c2d9f0a9e`.
Branch: `codex/30-budget-picker`.

## Automated checks

- `dotnet test tests/Finyte.IntegrationTests --filter BudgetApiTests --verbosity quiet`: 14 passed, none skipped, with `FINYTE_TEST_POSTGRES` pointing at a dedicated test database. The PostgreSQL case uses and removes its own random schema.
- `npm --prefix src/Finyte.Web run build`: passed (existing large-bundle warning).
- `npm --prefix src/Finyte.Web run lint`: passed.
- Coverage includes category availability/tenant isolation, removed categories, invalid/empty selections, overlapping category/tag matches, preview/saved-total parity, exclusions, account scope, period boundaries, examples, and a preview that writes no budget.

## Browser checks

Used the existing development launcher, URL `http://127.0.0.1:5186/`, preserved sample database, and confirmed API readiness plus Temporal worker polling.

At 402 × 874 CSS pixels, checked the actual app's empty category source, tag selection, populated preview, save and matching saved total. Removed only the temporary verification budget afterward. The live dataset has no category values, so grocery selection and shareable screenshots use the real BudgetEditor and Drawer with synthetic responses in `src/Finyte.Web/tests/budget-review.html`. That separate development entry point has no production import and its Axios adapter never calls the API or modifies user data.

Fixture checks: searchable grocery selection, selected chips persisting across searches, optional tags, empty selection blocking save, missing category/tag removal and recovery, no-match feedback, preview error blocking save, retry recovery, and successful save. The fixture provides display examples; backend tests and the live app independently verify integration/counting behavior.

All saved PNGs were reopened and inspected at their native 402 × 874 dimensions for wrapping, clipping, overlap and legibility. These are portrait viewport emulation, not real-device or Safari testing. The picker and preview are separate scroll positions of the same form.

- [Searchable picker — synthetic data](mobile-picker.png)
- [Populated preview — synthetic data](mobile-preview.png)
- [Empty matches — synthetic data](mobile-empty.png)

To reproduce after running the launcher from this worktree, copy `src/Finyte.Web/tests` into `/app/tests` in the development client container, then open `/tests/budget-review.html`. Use `?state=missing` for unavailable selections or `?state=error` for a preview failure that succeeds on retry. The fixture is not included in the production build.
