# Transaction imports

The Imports page (`/imports`) accepts single-account OFX exports, including the SGML and XML formats used by banks. Select an existing family account or create a manual account on the page. No Fiskil connection is required. Existing subscription requirements still apply.

Uploads are limited to 10 MB. The currency, when supplied by the file, must match the account. Transactions use the posting date from the export. Account balances remain the values entered when creating the account; importing historical transactions does not recalculate them.

Bank transaction IDs are hashed with the selected account ID for repeat-upload detection. Files without bank IDs use the posting date, amount, normalized description, and occurrence number, preserving repeated identical rows within an export. Overlapping exports without bank IDs cannot distinguish individual identical transactions beyond their occurrence counts.

Existing provider transactions are matched by date, amount, and normalized description; a single unambiguous provider transaction with the same date and amount can also be linked when descriptions differ. Linking preserves the provider ID and existing tags. New imports apply matching merchant tag rules and invalidate the family's dashboard projections. This matching applies during file import; later provider syncs retain their existing identity rules.

`POST /api/imports/ofx` accepts multipart fields `accountId` and `file`, returning imported, skipped, and total counts. `GET /api/imports` returns the most recent 100 runs for the authenticated family. Parse failures are recorded without importing partial data. PostgreSQL uploads lock the destination account and commit transactions, file identities, completion status, and projection invalidation together.

Apply the `AddTransactionFileImports` EF migration before running this feature. The Docker API configuration already enables migrations on startup. For a manually hosted API, use:

```powershell
dotnet ef database update --project src/Finyte.Data --startup-project src/Finyte.Api
```
