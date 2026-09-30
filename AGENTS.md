# NexFlow Agent Contract

NexFlow is a multi-tenant WhatsApp communication SaaS.

It is NOT an ERP, POS, inventory, accounting, payment, stock-management or logistics system.

## Product invariants

- AI interprets natural language; deterministic software validates, decides and persists.
- AI must never invent business facts, prices, products, services, locations, schedules or availability.
- CATALOG = products.
- SERVICES = services.
- ORDERS integrates with CATALOG but is independently licensed.
- RESERVATIONS integrates with SERVICES but is independently licensed.
- REQUESTS is an independent transversal workflow.
- Preserve strict `WorkspaceId` isolation.
- Backend is the security and business-rule authority.
- Frontend permissions never replace backend authorization.

## Backend baseline

Backend hardening A-H is considered stable.

Do not redesign or re-audit hardened backend architecture unless the current task proves a concrete integration defect.

- PostgreSQL owns transactional/durable infrastructure.
- Firestore stores business configuration/document-oriented business data.
- Redis is cache only.
- Inbound/outbound/business effects must remain durable and idempotent.
- Critical work must not depend only on memory or `Task.Run`.

## Frontend invariants

- Frontend contracts must exactly match current backend controllers/DTOs.
- Never invent API fields or permissions.
- Use `/me`/backend entitlements and capabilities as authorization metadata for UI.
- Server state belongs to TanStack Query.
- Do not duplicate server state in Zustand unless it is truly client/UI state.
- Query/cache keys must include tenant/workspace identity where applicable.
- Persisted cache must never leak data across users or workspaces.
- Mutations must invalidate/update only the affected query keys.
- Do not query Firestore directly for business data from frontend.
- Avoid `any`; preserve TypeScript contracts.
- Prefer React composition over inheritance.
- Shared UI primitives belong in reusable components, not duplicated pages.
- Never use `alert`, `confirm` or `prompt` for production UX after the shared dialog/toast system exists.

## Execution rules

The current repository is the source of truth.

For every task:

- Work only on the explicitly requested phase/scope.
- Do NOT audit the complete repository unless explicitly requested.
- Do NOT read roadmap/hardening documents unless the prompt explicitly asks for a specific one.
- Do NOT browse the web unless explicitly requested or local code is insufficient.
- Inspect existing implementation before adding abstractions.
- Reuse existing services, hooks, components and patterns when appropriate.
- Use targeted searches (`rg`) for direct callers/references.
- Do not fix unrelated issues.
- Do not perform cosmetic refactors outside scope.
- Do not modify `appsettings*`, secrets, documentation or Git unless explicitly requested.
- Do not create commits or push.
- Do not update package versions unless explicitly required.
- Do not advance to another phase.

## Build rules

Run builds only for projects touched by the task.

If backend changed:

`dotnet build backend/backend.slnx --no-restore`

Restore once only if required.

If frontend changed:

run the existing frontend build command using the current lockfile/package manager.

Do not upgrade dependencies merely to make a build pass.

If both changed, build both.

## Completion rule

A phase is complete only when every acceptance criterion in the current prompt is satisfied.

Before finishing:

1. inspect only the modified diff;
2. verify the phase acceptance criteria;
3. run required builds;
4. report real blockers, never assumed success.

Final response should contain only:
- files changed grouped by backend/frontend;
- acceptance criteria OK/PENDING;
- build results;
- real blocker if any.

Keep the final response concise.