# Household permissions

Audited 22 September 2026 against `main`, for issue #26. Every statement below was read from the endpoint
code, not from product intent. Re-run the audit before changing user-facing wording.

## How the audit was done

Role is resolved in `TenantResolver` from the Clerk `org_role` claim: `org:admin` becomes `Owner`, anything
else becomes `Member`. Searching the API for `TenantRole`, `currentTenant.Role` and `Forbid()` returns hits in
exactly one endpoint file, `FamilyEndpoints.cs`. Every other endpoint group authorises with
`RequireAuthorization()` plus tenant scoping only.

## What each role can do

| Capability | Owner | Member |
| --- | --- | --- |
| Invite a member, revoke an invitation, remove a member | Yes | No (403) |
| See every account in the household, including accounts another person added | Yes | Yes |
| See every transaction on every account | Yes | Yes |
| Rename accounts, change account type, set manual balances, change analytics scope | Yes | Yes |
| Create and edit tags, merchant rules, budgets, pay cycles, recurring payments | Yes | Yes |
| Import statements, confirm or dismiss internal transfers | Yes | Yes |
| Start a bank connection | Yes | Yes |
| Disconnect a bank connection | Only ones they added | Only ones they added |
| Disconnect a bank connection added by someone who has been removed | No | No |
| Start a subscription checkout, open the billing portal | Yes | Yes |

## Consequences worth stating plainly

**There is no per-account privacy.** Tenant scoping is the only boundary. An account one person adds is fully
visible to everyone else in the household, including its transactions and balances. Nothing in the data model
marks an account as personal.

**Members are not restricted.** Apart from managing who is in the household, a member can change anything an
owner can, including other people's account settings and transfer decisions.

**Removing someone does not stop their bank connection, and nobody left can stop it either.** This is the most
serious finding in this audit.

`ProviderConnectionEndpoints.Disconnect` matches the connection on `x.TenantMemberId == currentMemberId`, so
only the member who created a connection can revoke it. An owner who tries gets a 404, because the row is
never found — not a 403, so the UI cannot even distinguish "not yours" from "does not exist".

`FamilyEndpoints.RemoveMember` sets `member.RemovedAt` and calls Clerk to remove the user from the
organisation. It does not touch `ProviderConnections` at all.

Put together: once a member is removed they can no longer sign in, and the only account that was ever allowed
to revoke their connection is theirs. The connection is orphaned. Its Fiskil consent stays live, the sync
keeps pulling that person's bank data into a household they are no longer part of, and no one who remains has
any route to stop it from inside Finyte.

The only remaining remedies are outside the product: revoking the consent with the bank or with Fiskil
directly. `FiskilWebhookIngestor` will then mark the connection revoked when the provider reports it, which is
the only other place in the codebase that sets `ProviderConnectionStatus.Revoked`.

This is unresolved. Fixing it is a behaviour change, not a wording change — it needs a decision about who may
revoke a connection (owner, any member, or the creator plus the owner) and what `RemoveMember` should do to a
departing member's connections.

**Billing is not owner-only.** Any member can start a Stripe checkout or open the billing portal.

## Known gap

Finyte has no privacy model. Any future "personal" or "joint" grouping is a reporting filter and must be
described as one: grouping accounts does not restrict who can see them. Implementing real isolation would need
per-account access rules, a migration for existing data, and a decision about what an owner may see. That is
design work, tracked separately — do not let reporting-group wording imply it already exists.
