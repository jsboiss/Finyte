Replace typed budget categories with searchable category and optional tag pickers. Selected and unavailable criteria remain visible; empty or invalid selections cannot broaden spending. Before saving, show the matching total, explicit period and example transactions using the same eligibility query as saved budgets.

Closes #30.

Validation: 14 budget tests passed including PostgreSQL; frontend build and lint passed. Verified the live mobile preview/save journey and inspected 402 × 874 screenshots using the actual components with synthetic data. The live dataset has no categories; grocery selection is covered by the fixture and API tests.

Screenshots: [picker](mobile-picker.png), [preview](mobile-preview.png), [empty matches](mobile-empty.png). All use synthetic data. Replace these local links with immutable commit links when publishing.
