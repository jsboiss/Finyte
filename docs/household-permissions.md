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
| Start a subscription checkout, open the billing portal | Yes | Yes |

## Consequences worth stating plainly

**There is no per-account privacy.** Tenant scoping is the only boundary. An account one person adds is fully
visible to everyone else in the household, including its transactions and balances. Nothing in the data model
marks an account as personal.

**Members are not restricted.** Apart from managing who is in the household, a member can change anything an
owner can, including other people's account settings and transfer decisions.

**Disconnection is asymmetric.** `ProviderConnectionEndpoints` scopes disconnect by `TenantMemberId`, so the
person who added a bank connection is the only one who can remove it — the owner cannot. Removing that member
does not disconnect it either; `FamilyEndpoints.RemoveMember` sets `RemovedAt` and does not touch
`ProviderConnections`.

**Billing is not owner-only.** Any member can start a Stripe checkout or open the billing portal.

## Known gap

Finyte has no privacy model. Any future "personal" or "joint" grouping is a reporting filter and must be
described as one: grouping accounts does not restrict who can see them. Implementing real isolation would need
per-account access rules, a migration for existing data, and a decision about what an owner may see. That is
design work, tracked separately — do not let reporting-group wording imply it already exists.
