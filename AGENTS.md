# Working in this repository

- Do not add or update documentation unless a behavioural rule changed. Rules live in `docs/README.md`, one or two lines each. Design rationale, screenshots and verification go in the pull request description, never in the repo.
- Do not create per-issue folders (`docs/issue-NN/`) or commit screenshots.
- Do not add code comments.
- Branch names are plain (`issue-41-internal-transfers`), never prefixed with a tool name.
- Run the backend tests with `FINYTE_TEST_POSTGRES` pointing at a disposable database on the local `finyte-postgres` container; a run that reports skipped tests has not exercised PostgreSQL.
- Never post pull request comments, review replies or review requests on the user's behalf.
