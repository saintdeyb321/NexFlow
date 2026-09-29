# NexFlow Agent Contract

NexFlow is a multi-tenant WhatsApp communication SaaS.

It is NOT an ERP, POS, inventory, accounting, payment, stock-management or logistics system.

## Product invariants

- AI interprets natural language; deterministic software validates, decides and persists.
- AI must never invent business facts, prices, products, services, locations, schedules or availability.
- CATALOG = products.
- SERVICES = services.
- ORDERS integrates with CATALOG but is an independent licensed module.
- RESERVATIONS integrates with SERVICES but is an independent licensed module.
- REQUESTS is an independent transversal workflow.
- Preserve strict `WorkspaceId` isolation.

## Backend invariants

- PostgreSQL owns transactional/durable infrastructure such as Inbox, Outbox, reservations, licenses and notifications.
- Firestore stores business configuration and current document-oriented business data.
- Redis is cache only, never the durable source of conversation state.
- Accepted inbound messages must not be lost.
- Business effects and outbound messages must be idempotent.
- Messages from the same conversation must be processed in order.
- AI `FromMe` echoes must never be interpreted as human takeover.
- Critical work must not depend only on in-memory queues or `Task.Run`.

## Execution rules

The current repository is the source of truth.

For every task:

- Work only on the scope explicitly requested by the user.
- Do NOT audit the complete repository unless explicitly requested.
- Do NOT read `docs/BACKEND_HARDENING_PLAN.md` unless explicitly requested.
- Do NOT browse the web unless explicitly requested or implementation is impossible from local code.
- Inspect existing implementations before creating new abstractions.
- Reuse existing services/interfaces whenever possible.
- Use targeted searches (`rg`) instead of broad exploration.
- Do not modify frontend, tests or `appsettings*` during backend tasks unless explicitly requested.
- Do not create commits or push to GitHub.
- Do not update roadmap/documentation unless explicitly requested.
- Do not run `dotnet restore` unless required by missing assets/dependencies.
- Run one final build after completing all requested changes:
  `dotnet build backend/backend.slnx --no-restore`
- If that build fails because restore is required, restore once and retry.
- Do not mark work complete merely because it compiles.

## Completion rule

A task is complete only when every acceptance criterion from the current user prompt is satisfied.

Before finishing:

1. inspect the modified diff;
2. check the explicit acceptance criteria;
3. run the final build once.

Final response:
- files changed;
- build result;
- unresolved blocker, if any.

Maximum final response: 10 lines.