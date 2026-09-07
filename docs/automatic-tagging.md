# Automatic transaction tagging

Merchant rules now use one service for provider transaction upserts, new OFX transactions, and rule changes. The Finance prototype only added tags; Finyte reconciles automatic assignments as merchants and rules change, while preserving the user's choices.

## Matching

A rule matches complete leading words of the supplied merchant name, falling back to the description only when the merchant name is missing. Case, punctuation, and repeated whitespace are normalized; Unicode letters and numbers are retained. `Coles` matches `COLES 4568 ASCOT AU`, but not `Colesworth`. Place names and common business words are kept: a rule for `Coffee Brisbane` does not silently become a rule for every `Coffee` merchant. Empty/punctuation-only rules are rejected. Old rule keys are normalized from their displayed names when rules are next used.

Multiple rules may produce the same tag. One assignment is stored, supported by the most specific matching rule (then rule ID for a deterministic tie). Deleting that rule keeps the tag if another matching rule still supports it. Provider merchant corrections remove obsolete automatic tags and apply current matches.

## Provenance and manual choices

Assignments expose `source`, `merchantRuleId`, and `merchantRuleName`:

- `merchant-rule`: automatic; current rules control its continued presence.
- `manual`: explicitly added or kept by the user; rule changes do not remove it.
- `legacy`: source was not recorded before this migration; preserved conservatively.
- `system`: reserved for system features. Internal transfers continue to use their dedicated review records, not a special tag.

Selecting an additional tag makes that addition manual. Submitting an unchanged selection does not turn automatic tags into manual tags. **Keep manual** explicitly pins a selected automatic or legacy tag. Unchecking any assigned tag records a per-transaction/tag exclusion, preventing later syncs and rule changes from re-adding it. Other automatic tags remain eligible. Rechecking that tag adds it manually and removes its exclusion.

**Restore removed automatic tags** clears those exclusions and reevaluates current rules. Existing manual and legacy assignments remain. Restoring also allows future rules to apply tags that were previously removed. There is no migration-time inference of existing assignments' original source.

## API

- `GET /api/transactions`: each transaction tag includes its provenance and supporting rule; `automaticTagExclusions` lists tag IDs explicitly removed from future automation.
- `PUT /api/transactions/{id}/tags`: `{ "tagIds": [...], "manualTagIds": [...] }`; `manualTagIds` is optional and must be a subset of selected tags. New selections are manual automatically.
- `POST /api/transactions/{id}/tags/restore-automatic`: clears exclusions, reapplies current rules, returns current tags.
- `POST /api/merchant-tags`: creates a rule and reconciles the family's existing ledger.
- `PUT /api/merchant-tags/{id}`: `{ "merchantName": "Coles", "tagId": "..." }`; edits the rule and reconciles the ledger.
- `DELETE /api/merchant-tags/{id}`: removes the rule and retracts only unsupported automatic assignments.

All operations resolve the active family server-side. Tag and rule IDs from another family are rejected. Mutation responses carry authoritative provenance; the frontend does not duplicate merchant matching or infer automatic tags optimistically.

## Persistence and projections

Apply `AddTagProvenanceAndExclusions` before running this branch. Existing tag assignments receive `legacy`. Exclusions have a composite transaction/tag key and cascade on transaction or tag deletion. Supporting rule references are nullable; reconciliation handles rule deletion before committing.

PostgreSQL uses one transaction-scoped advisory lock per family for tag mutations, rule changes, OFX insertion, and provider transaction upserts. This prevents a sync from undoing a concurrent manual removal and prevents rules changing partway through ingestion. Provider responses are fetched before acquiring the lock. Rule reconciliation processes 500 transactions per batch, detaches processed entities, and commits all batches and projection invalidation together. A tag-only provider change sets the normal sync change summary so Temporal projection refreshes still run.

New OFX rows receive automatic tags. Duplicate/relinked OFX rows preserve their existing assignments and exclusions; rule create/edit/delete already reconcile existing rows, and provider upserts also reconcile unchanged transactions. Merely uploading the same file is not an override-reset action.

Regression coverage includes provider/OFX parity, repeat sync/import, changed merchants, overlapping rules, explicit manual promotion, sticky removals/restoration, changed/deleted rules, legacy preservation, projection invalidation, and family isolation.
