---
name: fiskil-api
description: Use local Fiskil Data API documentation when building, reviewing, debugging, or planning integrations with Fiskil, including authentication, auth sessions, consent flows, Link SDK, banking data, energy data, identity data, income, permissions, pagination, errors, webhooks, API versioning, and v1/v2 endpoint behavior.
---

# Fiskil API

## Overview

Use this skill to ground Fiskil Data API work in the local documentation snapshot stored under `references/`. Prefer the local docs before relying on memory, especially for endpoint names, auth headers, consent/session lifecycle, permissions, domain-specific data shapes, pagination, errors, and v1/v2 differences.

## Documentation Map

Start with these local references:

- `references/manifest.md`: generated index of downloaded Fiskil Data API pages.
- `references/data-api/guides/getting-started/start-exploring.mdx`: orientation and first setup steps.
- `references/data-api/guides/getting-started/quick-start.mdx`: first integration flow.
- `references/data-api/guides/getting-started/authentication.mdx`: authentication concepts and examples.
- `references/data-api/guides/core-concepts/`: API protocols, auth sessions, consents, end users, testing, versioning, and webhooks.
- `references/data-api/guides/data-domains/`: banking, energy, identity, and income guides.
- `references/data-api/guides/link-widget/`: Link SDK and consent-flow UI integration.
- `references/data-api/api-reference/`: API reference overview, domain pages, errors, permissions, pagination, and endpoint pages.

Use `rg` over `references/` to find exact endpoint names, permissions, request/response examples, and version-specific pages.

## Workflow

1. Identify the Fiskil task: auth, user/session setup, consent flow, banking, energy, identity, income, webhooks, errors, pagination, or version migration.
2. Read the smallest relevant guide first.
3. Read the matching API reference page before implementing endpoint calls or payload handling.
4. Check version-specific pages under `v1-0-0` or `v2-0-0` when behavior, paths, fields, or examples may differ.
5. Reflect uncertainty explicitly if the local snapshot does not answer a detail. Use the source URL in the page heading to verify online when freshness matters.

## Implementation Guidance

- Treat authentication, permissions, consent status, and webhook validation as security-sensitive. Verify against local docs before changing behavior.
- Prefer documented Fiskil lifecycle concepts over inferred app-specific shortcuts: end users, auth sessions, consents, linked accounts, and data-domain permissions are distinct.
- For generated clients or DTOs, confirm whether the code targets v1 or v2 pages and avoid mixing schemas from both versions.
- When implementing pagination, error handling, retries, or webhook processing, consult the dedicated reference pages even if an endpoint example looks sufficient.
- Keep source links or doc page paths in code comments only when they clarify non-obvious API behavior.

## Updating Local Docs

Run `scripts/refresh_docs.ps1` from the skill folder to refresh the local Markdown snapshot from `https://docs.fiskil.com/llms.txt`. The script downloads Data API guides, API reference pages, and changelog pages as `.mdx`, then regenerates `references/manifest.md`.
