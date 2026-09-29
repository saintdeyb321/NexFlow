\# NexFlow — Agent Instructions



\## Scope



These instructions apply to the whole repository.



NexFlow is a multi-tenant SaaS communication automation platform centered on WhatsApp. Its purpose is to automate customer-facing communication, business information, reservations, requests and product-interest lists.



NexFlow is NOT an ERP, POS, inventory system, accounting platform, payment processor, stock manager, logistics platform or e-commerce engine.



Before making architectural changes, read:



\- `docs/NEXFLOW\_PRODUCT.md`

\- `docs/BACKEND\_HARDENING\_PLAN.md`



\## Core product rules



1\. AI interprets and writes; deterministic software decides and executes.

2\. Never allow an LLM to invent business facts, prices, availability, locations, schedules or reservations.

3\. `CATALOG` means PRODUCTS.

4\. `SERVICES` means SERVICES.

5\. `CATALOG` and `SERVICES` may share technical infrastructure but remain commercially independent modules.

6\. `ORDERS` integrates with `CATALOG`; it is not contained by it.

7\. `RESERVATIONS` integrates with `SERVICES`; it is not contained by it.

8\. `REQUESTS` is a transversal workflow for procedures, commercial inquiries, support and human attention.

9\. Do not introduce ERP/POS/inventory/stock/payment/accounting behavior.

10\. Every operation must preserve `WorkspaceId` tenant isolation.



\## Backend architecture



Preserve:



\- `NexFlow.API`

\- `NexFlow.Application`

\- `NexFlow.Domain`

\- `NexFlow.Infrastructure`



Main persistence:



\- PostgreSQL: transactional/control-plane data, Inbox, Outbox, reservations, licenses, notifications.

\- Firestore: business configuration, catalog/services, conversations, requests/orders where currently designed.

\- Redis: cache only; never the sole source of durable conversational state.



Integrations:



\- Evolution API: WhatsApp transport.

\- n8n: external automation/integration layer.

\- Gemini/Groq: interpretation/writing only.



\## Reliability invariants



A message acknowledged by the webhook must not be lost.



The same inbound message must not create duplicate business effects.



Messages belonging to the same conversation must be processed in order.



Outbound messages must be idempotent.



Redis loss must not destroy a conversation.



Outbox and Inbox workers must recover stale `Processing` records.



Retries must use durable state and bounded backoff.



Do not use `Task.Run` or in-memory queues for business-critical durable work.



\## Conversation invariants



Conversation state must have one durable source of truth.



Redis may cache that state but must not own it.



A human takeover must not delete conversational context.



AI-originated `FromMe` echoes must never be mistaken for human takeover.



A real WhatsApp message sent manually by the business owner must switch/maintain Human mode correctly.



An active transactional flow has priority over generic greeting/acknowledgement shortcuts.



\## Development rules



\- Inspect existing code before creating files or abstractions.

\- Never implement something that already exists under another name.

\- Prefer modifying existing architecture over duplicating services.

\- Avoid speculative abstractions.

\- Keep changes minimal and cohesive.

\- Preserve backward-compatible API contracts unless fixing a documented defect.

\- Do not silently change module licensing semantics.

\- Do not modify `appsettings\*`, secrets or production configuration unless explicitly requested.

\- Do not add or modify automated tests unless explicitly requested.

\- Never suppress exceptions merely to make a flow appear successful.

\- Never represent infrastructure failure as business `NotFound`.

\- Never fabricate external provider IDs.



\## Required validation after code changes



At minimum run:



`dotnet restore backend/NexFlow.sln`



`dotnet build backend/NexFlow.sln --no-restore`



If the solution path differs, discover the correct `.sln` and use it.



Do not finish with known compiler errors.



\## Working style



For each assigned sprint:



1\. Read only the files relevant to that sprint plus direct dependencies.

2\. Verify whether the reported problem still exists in the current repository.

3\. Do not blindly implement an audit recommendation if current code already solves it.

4\. Implement the smallest complete fix.

5\. Check all callers/interfaces/DI registrations affected.

6\. Build.

7\. Review the git diff for regressions or unrelated changes.

8\. Update `docs/BACKEND\_HARDENING\_PLAN.md` only by marking completed work; do not rewrite history.



\## Output discipline



Keep final responses short.



Report only:



\- sprint completed;

\- important files changed;

\- validation/build result;

\- blockers or decisions that genuinely require user input.



Do not print entire modified classes unless explicitly requested.

