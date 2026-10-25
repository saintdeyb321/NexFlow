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

## NexFlow Agent Runtime 4.0

- Preserve the existing AiRouter and AI provider abstraction.
- Use one shared agent runtime with isolated context per workspace.
- Business-specific knowledge, permissions and conversation state
  must remain scoped to the authorized workspace.
- AI handles semantic interpretation and may compose natural
  responses from verified facts.
- Deterministic backend services remain the sole authority for
  business operations, validation, permissions and persistence.
- Do not build large keyword trees, synonym dictionaries or
  hardcoded conversation branches to simulate intelligence.
- Explicit menus are optional aids, not mandatory conversation paths.
- Prefer natural questions that let customers provide information
  freely, including their preferred reservation time.
- ORDERS creates commercial requests and notifies the business
  for review and customer coordination. It does not process payments
  or guarantee stock availability.
- Refactor conversational orchestration when justified by the
  current phase, without redesigning unrelated hardened components.
- The future employee-management module is outside the scope
  of Agent Runtime 4.0 and must not be exposed as AI knowledge.

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

## Conversation UX invariants

- Conversations must guide the consumer with real deterministic options instead of requiring them to guess valid answers.
- An active transactional goal must not suppress read-only factual questions. Answer safe informational interruptions and then resume the existing draft without losing context.
- Never confuse a closed day, out-of-hours request, occupied slot and unavailable service. Preserve their real domain meaning.
- When asking the consumer to choose, show only valid options from real business data and ask one concrete next question.
- Broad product/service queries prefer a current generated artifact. If unavailable, show at most 5 real offerings and notify the business without notification spam.
- Specific product/service questions should answer the requested fact instead of sending an unnecessary full catalog.
- LLMs extract intent, entities and conversational directives. Deterministic software decides business facts, options, transitions and availability.
- Documents/media must use the same durable and idempotent outbound lifecycle as text messages. Flows must never call the provider directly.
- Informational interruptions must not silently clear reservation/order drafts.

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

For audit-only phases:
- Do not modify application code.
- Do not run builds unless explicitly requested.
- Deliver the requested diagnostic Markdown document.
- Report confirmed findings, hypotheses, severity, affected
  components, architectural recommendations and blockers.
- Stop before implementing the next phase.

Keep the final response concise.

