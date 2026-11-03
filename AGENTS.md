# Exchange maintenance

Use master. The exchange repository is NovcicExchange; ../docs is a separate public repository. Preserve Docker project names, database volumes and local chain state when changing workspace layout.

Read BRANDING.md before interface changes. Keep the shared Meadow light design and synchronize tokens/docs with scripts/sync-docs.py. Do not commit task.txt or operational secrets.

Financial amounts use decimal arithmetic and exact JSON strings. Financial mutations and idempotency receipts must commit atomically. Public books expose aggregated levels only. Never fabricate liquidity or multiply quoting budgets by quote-level count.

API changes require an audited export-api.py catalog update, regenerated public contract, updated sibling documentation and focused validation. Run python scripts/check-contract.py to detect source/contract drift. Record implementation and verification in IMPLEMENTATION.md.
