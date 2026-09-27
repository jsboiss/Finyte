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

Copy `.env.example` to `.env` in the repository root and fill in the four values
provided privately by the maintainer:

- `FINYTE_FISKIL_CLIENT_ID` and `FINYTE_FISKIL_CLIENT_SECRET`: shared sandbox credentials.
- `FINYTE_FISKIL_SANDBOX_END_USER_ID`: the shared sandbox end user.
- `FINYTE_FISKIL_SANDBOX_CONSENT_ID`: that user's active sandbox bank arrangement ID.

Leave `FINYTE_FISKIL_SANDBOX_ENABLED=true` to enable the button. Keep the quotes
around credential values. `.env` is ignored by Git and excluded from Docker build
contexts; only the blank template is committed.

Contributors using these shared settings do not need their own Fiskil account
or consent. Each local instance imports the same provider sample data into its
own database. Resetting local data does not delete data in Fiskil. Revoking the
shared consent through **Disconnect** affects everyone using it.

The maintainer completes the consent once through Fiskil Console, selecting the sandbox bank
and desired sample accounts. The configured end user must contain only accounts
under that consent. Consent renewal still requires Fiskil's consent flow.

Run `scripts/dev.ps1 start` (or `StartApp.cmd`). Docker Compose supplies the
credentials to both API and worker. Restart using the launcher after changing
`.env`. Clicking the button calls the API and existing Temporal sync pipeline;
it does not run the launcher.

Shell environment variables override `.env` values. When `.env` exists, the
launcher does not load .NET user secrets, including when a value is blank. This
prevents accidentally mixing credentials and consent IDs from different setups.
Without `.env`, existing Windows setups can still use API-project user secrets:
`Fiskil:ClientId`, `Fiskil:ClientSecret`, `Fiskil:Sandbox:Enabled`,
`Fiskil:Sandbox:EndUserId`, and `Fiskil:Sandbox:ConsentId`.

GitHub Actions secrets are for CI runs; they are not required for local setup.

Each reset fetches the provider's current sample data; it does not generate new
bank events. It exercises fresh transaction ingestion, tagging, transfer
processing and projection refresh, rather than the inbound webhook delivery
mechanism. The database schema must match the branch being tested.
