# Requested changes

Capture ideas and requested improvements here while other work is underway. Entries are notes for later, not instructions to implement immediately. Implement them when explicitly requested.

## Pending

### Imports: show existing accounts

- **Requested:** 2026-09-14
- **Status:** Noted — not implemented
- **Problem:** When using “Add an account for imports”, it is hard to tell which accounts already exist. Currently, users must open the account dropdown under “Import transactions” to check.
- **Requested change:** Show a visible list of existing accounts in the Imports module, so users can check what has already been created while adding accounts.
- **Expected outcome:** Users can identify existing accounts without opening the import dropdown, and newly created accounts appear in the list.

### Imports: make file selection clearer

- **Requested:** 2026-09-14
- **Status:** Noted — not implemented
- **Problem:** It is not intuitive that clicking the “OFX file” field opens the file selector.
- **Requested change:** Make the file-selection action obvious, for example with a clearly labelled “Choose OFX file” or “Browse files” button.
- **Expected outcome:** Users can immediately identify how to select an export, and see the selected filename afterwards.

### Recurring payments: guide initial setup

- **Requested:** 2026-09-14
- **Status:** Noted — not implemented
- **Problem:** On first entering Recurring payments, it is not obvious what to do or how to get started, even after importing transactions.
- **Requested change:** Provide clearer initial setup guidance: explain the difference between discovering patterns and tracking payments, direct users to discovery or manual creation, and explain how reviewing a suggestion leads to a tracked series and payment review.
- **Expected outcome:** A first-time user can understand why the tracked list is empty and complete setup without needing instructions outside the app.

### Recurring payments: filter discovery by account

- **Requested:** 2026-09-14
- **Status:** Noted — not implemented
- **Problem:** Discovery searches all eligible accounts together, making it harder to investigate payments from a specific account.
- **Requested change:** Add an account selector to recurring-payment discovery, allowing users to search one account or all eligible accounts.
- **Expected outcome:** Users can review patterns for Main, Joint or Savings independently, with the selected account scope clearly shown.

### Recurring payments: make detected patterns easier to browse at scale

- **Requested:** 2026-09-14
- **Status:** Noted — not implemented
- **Problem:** The detected recurring-patterns list requires too much scrolling as the number of suggestions grows.
- **Requested change:** Provide a more compact, scannable list, with detailed evidence expandable or opened on demand. Consider search, filtering and sorting to help users find relevant patterns.
- **Expected outcome:** Users can efficiently scan and review many detected patterns without scrolling through every suggestion's full details.

### App-wide: reduce explanatory text in the initial view

- **Requested:** 2026-09-14
- **Status:** Noted — not implemented
- **Problem:** All modules/pages show too much explanatory text by default. The volume is visually overwhelming and makes the initial experience harder to scan.
- **Specific example:** The Pay cycles page floods the user with too much text up front. Use it as a concrete starting point when reviewing visible explanations and moving secondary guidance into on-demand help.
- **Dashboard example:** The always-visible paragraphs explaining transfer exclusions, balance movements and account preferences feel like random text on the dashboard. Move these explanations and related management links into contextual help or relevant controls. Avoid showing a zero-state message such as “0 accounts excluded from combined spending and income” when there is nothing actionable to communicate.
- **Design preference:** Prioritise the user's data and actions. Keep visible instructions short and only include what is necessary for the current step. Detailed explanations are welcome when available on demand through help icons, expandable sections or contextual help.
- **Requested change:** Review explanatory copy across the app and move secondary guidance out of the initial view. Preserve clear labels, actionable errors and essential decision information. Make help accessible on touch devices and by keyboard, rather than relying only on hover.
- **Expected outcome:** Pages feel concise and approachable on first use, with fuller explanations available when users need them. Initial setup guidance should follow this preference too, using brief next steps rather than blocks of prose.

### Recurring payments: consider a subscription calendar view

- **Requested:** 2026-09-14
- **Status:** Idea noted — not implemented
- **Suggested improvement:** Offer an optional calendar view to visualise subscriptions and recurring payment dates.
- **Expected outcome:** Users can see when payments are expected and spot clusters of upcoming costs at a glance, with payment details available on demand.

### App-wide: prioritise mobile usability

- **Requested:** 2026-09-14
- **Status:** Noted — not implemented
- **Problem:** Much of the UI design has overlooked mobile support, leaving pages and interactions insufficiently adapted to smaller screens.
- **Design preference:** Treat mobile as a primary supported experience when designing new features and improving existing pages.
- **Requested change:** Review layouts, navigation, forms, lists, charts and help controls on phone-sized screens. Address horizontal overflow, cramped controls, excessive scrolling and interactions that depend on hover. Verify key user journeys on mobile as well as desktop.
- **Expected outcome:** All modules remain readable and usable on a phone, with touch-friendly controls and layouts suited to the available space.

### Internal transfers: automate recognition between configured accounts

- **Requested:** 2026-09-14
- **Status:** Noted — not implemented
- **Problem:** Internal transfer review feels like a heavy manual operation. Requiring users to confirm transfers between their already configured accounts is unintuitive and adds unnecessary work.
- **Requested change:** Automatically identify and classify internal transfers between configured household accounts where the evidence supports a clear match. Make review an exception for ambiguous or conflicting matches rather than a mandatory step for every transfer.
- **Expected outcome:** Routine transfers are handled without repeated confirmation. Users can inspect and correct automatic classifications when needed, with concise explanations available on demand.

### Navigation: organise sidebar items by purpose

- **Requested:** 2026-09-14
- **Status:** Noted — not implemented
- **Problem:** Sidebar items mix everyday finance features with account administration without clear grouping. Billing feels randomly placed among the app's financial features.
- **Requested change:** Group and order navigation by user purpose. Move Finyte subscription billing and Connections into account/profile settings or a similar administrative area, keeping everyday household finance features together. Apply the same organisation consistently to mobile navigation.
- **Expected outcome:** Users can find related features together and understand where to manage their Finyte account and subscription.

### Transactions: open filters without displacing the list

- **Requested:** 2026-09-14
- **Status:** Noted — not implemented
- **Problem:** Opening filters pushes the entire transaction list down, disrupting browsing and taking up too much of the page.
- **Requested change:** Use a desktop side panel or another filter presentation that does not push the list down. Design a suitable mobile interaction, such as a bottom sheet or dedicated filter overlay, with touch-friendly controls.
- **Expected outcome:** Users can adjust filters without losing their place in the transaction list. Keep applied filters and the clear/reset action easy to find when the panel is closed.

### App-wide: standardise icons and build a shared icon service

- **Requested:** 2026-09-14
- **Status:** Noted — not implemented
- **Requested change:** Establish a standard icon set and library, supported by a shared icon service/component layer used throughout the app.
- **Scope:** Define consistent icon mappings, sizes, stroke weights, colours, accessibility behaviour and fallbacks. Review existing icons and consolidate their usage through the shared layer.
- **Expected outcome:** Icons look and behave consistently across desktop and mobile, with one place to manage their selection and presentation.
