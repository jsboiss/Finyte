# Sandbox reset and sync

In a development instance, open **Connections → Reset data and sync sandbox**.
Confirm the reset to clear the current household's transactions, statement import
history, transfer matches, recurring-payment configuration, pay-cycle profiles,
sync history and cached reports. Tags, merchant tagging rules and budgets are
retained, along with budget account selections. Sandbox accounts and accounts
referenced by budgets keep their identities; their balances are cleared. Other
accounts are removed. Other households are not reset.

The API verifies the configured sandbox consent and available accounts before
clearing anything. Clearing and enqueueing the replacement sync share a database
transaction. The normal Temporal worker then fetches accounts, balances and
transactions, applies tagging rules and refreshes projections. A remote failure
after the reset commits can leave the household empty or partially populated;
the panel displays each stage and any errors. Do not use this on a household
whose financial history you need to keep without a backup.

Only a household owner can reset. Existing provider jobs must finish or be
cancelled first, and other active bank connections must be disconnected. This
tool is absent from production APIs and production frontend builds.

## Local setup

Store the following in the API project's .NET user secrets (never in Git):

- `Fiskil:ClientId` and `Fiskil:ClientSecret`: sandbox workspace credentials.
- `Fiskil:Sandbox:Enabled`: `true`.
- `Fiskil:Sandbox:EndUserId`: a dedicated sandbox end user.
- `Fiskil:Sandbox:ConsentId`: that user's active sandbox bank arrangement ID.

Complete the consent once through Fiskil Console, selecting the sandbox bank
and desired sample accounts. The configured end user must contain only accounts
under that consent. Consent renewal still requires Fiskil's consent flow.

Run `scripts/dev.ps1 start` (or `StartApp.cmd`). The launcher passes credentials
to both API and worker without printing them. The corresponding Compose
environment overrides are `FINYTE_FISKIL_CLIENT_ID`,
`FINYTE_FISKIL_CLIENT_SECRET`, `FINYTE_FISKIL_SANDBOX_ENABLED`,
`FINYTE_FISKIL_SANDBOX_END_USER_ID`, and `FINYTE_FISKIL_SANDBOX_CONSENT_ID`.

Each reset fetches the provider's current sample data; it does not generate new
bank events. It exercises fresh transaction ingestion, tagging, transfer
processing and projection refresh, rather than the inbound webhook delivery
mechanism. The database schema must match the branch being tested.
