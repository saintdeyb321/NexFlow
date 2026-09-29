\# NexFlow Backend Hardening Plan



Source of truth: current repository. Audit findings are hypotheses to verify before modifying code.



Do not modify frontend, tests or `appsettings\*` during this plan.



\## Completion target



Backend is considered functionally closed when:



\- accepted inbound messages cannot be lost;

\- same inbound cannot produce duplicate business effects;

\- messages from one conversation are processed in order;

\- outbound responses are idempotent;

\- AI/human `FromMe` messages are correctly distinguished;

\- Redis loss does not destroy conversation state;

\- AI never invents business facts;

\- reservations, orders and requests preserve context;

\- worker `Processing` states are recoverable;

\- all business endpoints enforce workspace/module/capability authorization;

\- there is no confirmed dead/duplicate architecture left;

\- solution builds with zero compiler errors.



\---



\## Sprint 01 — Atomic Inbound Claim [COMPLETADO]



Problem:

`FOR UPDATE SKIP LOCKED` claim may not cover SELECT + Processing update in one transaction.



Goal:

Exactly one worker claims each inbound record.



Work:

\- transactional/atomic claim;

\- Pending/Failed-ready/stale-Processing selection;

\- processing lease;

\- bounded polling.



Primary files:

\- `InboundMessageRepository.cs`

\- `IInboundMessageRepository.cs`

\- `InboundMessage.cs`

\- `InboundMessageWorker.cs`

\- `NexFlowDbContext.cs`



\---



\## Sprint 02 — Per-conversation ordering [COMPLETADO]



Problem:

Worker may process messages from the same consumer concurrently.



Goal:

Messages within a conversation are strictly sequential while different conversations may run concurrently.



Work:

\- group/partition by workspace + phone/conversation;

\- full-turn lock;

\- multi-instance-safe locking if necessary.



Primary files:

\- `InboundMessageWorker.cs`

\- `ProcessIncomingMessageCommandHandler.cs`

\- `ConversationStateService.cs`



\---



\## Sprint 03 — Inbound idempotency and tenant traceability [COMPLETADO]



Problem:

duplicate webhook insert may fail; WorkspaceId is not consistently persisted; malformed payload can be completed.



Work:

\- persist WorkspaceId;

\- duplicate webhook => idempotent OK;

\- malformed => Failed/DeadLetter;

\- expose retry lifecycle consistently.



Primary files:

\- `EvolutionWebhookController.cs`

\- `InboundMessageRepository.cs`

\- `IncomingMessageGuard.cs`

\- `InboundMessage.cs`



\---



\## Sprint 04 — Outbound idempotency and FromMe origin [COMPLETADO]



Problem:

AI-generated provider IDs are not recorded early enough; own Evolution echo may be mistaken for human input.



Work:

\- durable provider-message origin;

\- idempotency key persistence;

\- distinguish Consumer / NexFlowAI / NexFlowHuman / WhatsAppHuman;

\- never fabricate provider IDs;

\- propagate HTTP status from Evolution.



Primary files:

\- `OutboundMessageService.cs`

\- `EvolutionMessageGateway.cs`

\- `IConversationRepository.cs`

\- `FirestoreConversationRepository.cs`

\- `IConversationCache.cs`

\- `RedisConversationCache.cs`

\- `ConversationStateService.cs`



\---



\## Sprint 05 — Single durable ConversationState



Problem:

Order writes durable state while Reservation/Handoff still depend partly on Redis.



Goal:

One durable conversational source of truth; Redis is cache only.



Work:

\- central context store abstraction;

\- durable Save/Get/Clear;

\- migrate Reservation, Order and Handoff;

\- deleting conversation removes durable state and cache.



Primary files:

\- `IConversationStateRepository.cs`

\- `FirestoreConversationStateRepository.cs`

\- `ContextRecoveryService.cs`

\- `ConversationStateService.cs`

\- `HumanHandoffService.cs`

\- `AiResponseOrchestrator.cs`

\- `ConversationsController.cs`

\- `RedisConversationCache.cs`



\---



\## Sprint 06 — Conversation lifecycle / fast rules



Problem:

greetings and acknowledgements can intercept active workflows.



Work:

\- shortcuts only when no active goal;

\- explicit cancel/reset intents;

\- draft-specific clear helpers.



Primary files:

\- `ConversationStateService.cs`

\- conversation context model

\- `ContextRecoveryService.cs`



\---



\## Sprint 07 — AI / Knowledge deterministic routing



Problem:

FAQ/location/hours/general knowledge routing is inconsistent; infrastructure errors may look like NotFound.



Work:

\- explicit knowledge dispatch by typed intent;

\- Found / NotFound / Unavailable distinction;

\- no LLM fallback when business facts cannot be loaded;

\- verify intent against enabled modules;

\- remove ineffective intent mutation.



Primary files:

\- `AiInterpreter.cs`

\- `AiRouter.cs`

\- `AiResponseOrchestrator.cs`

\- `ConversationFlows.cs`

\- `KnowledgeService.cs`

\- knowledge models/interfaces



\---



\## Sprint 08 — Orders multi-turn



Problem:

Orchestrator clears ORDER goal; follow-ups rely on LLM reclassification; items use generic IDs.



Work:

\- OrderFlow owns lifecycle;

\- CurrentGoal ORDER persists;

\- structured draft;

\- add/remove/change/list/cancel/submit;

\- resolve real Catalog products;

\- explicit unresolved products;

\- no automatic ERP behavior;

\- no false human-handoff message;

\- explicit PendingQuote rather than price zero.



Primary files:

\- `OrderFlow.cs`

\- `AiResponseOrchestrator.cs`

\- `AiInterpreter.cs`

\- `OrderRecord.cs`

\- `OrderStatus.cs`

\- `IOrderRepository.cs`

\- `FirestoreOrderRepository.cs`

\- `OrdersController.cs`



\---



\## Sprint 09 — Requests idempotency and lifecycle



Problem:

SourceMessageId is not fully persisted; status lookup uses legacy behavior; assignment is not membership-validated.



Work:

\- persist SourceMessageId;

\- pass inbound message ID from conversation flow;

\- GetById;

\- valid state transitions;

\- membership validation on assignment.



Primary files:

\- `RequestService.cs`

\- `RequestRecord.cs`

\- `ConversationFlows.cs`

\- `IRequestRepository.cs`

\- `FirestoreRequestRepository.cs`

\- `RequestsController.cs`

\- `IMembershipRepository.cs`



\---



\## Sprint 10 — Reservations hardening



Work:

\- deterministic date/time parsing;

\- proper PostgreSQL serialization failure detection;

\- clarify ReservationStatus;

\- validate Npgsql concurrency mechanism;

\- proper 400/404/409 mapping;

\- durable context cleanup;

\- avoid full catalog reads for service by ID.



Primary files:

\- `ReservationEngine.cs`

\- `Reservation.cs`

\- `ReservationStatus.cs`

\- `ReservationRepository.cs`

\- reservation configuration

\- `ReservationsController.cs`

\- reservation conversation flow



\---



\## Sprint 11 — Catalog / Services integrity



Work:

\- active-only customer queries;

\- validate name/price/currency/category;

\- validate active category;

\- validate location IDs;

\- typed location scope;

\- service duration/reservation invariants;

\- repository queries instead of unnecessary full collection reads.



Primary files:

\- `OfferingService.cs`

\- offering DTOs

\- `ICatalogRepository.cs`

\- `FirestoreCatalogRepository.cs`

\- `CatalogController.cs`

\- `ServicesController.cs`



\---



\## Sprint 12 — Locations / Hours / Categories integrity



Work:

\- real delete/replace hours;

\- validate location before hours;

\- POST=create and PUT=update semantics;

\- atomic main-location invariant;

\- block/soft-delete locations with future reservations;

\- category delete checks active and inactive offerings.



Primary files:

\- `BusinessController.cs`

\- location command handler

\- location/hours/catalog repositories and interfaces



\---



\## Sprint 13 — Outbox resilience



Problem:

Processing records can become stuck and retry logic is duplicated.



Work:

\- Processing lease/recovery;

\- NextRetryAt / bounded exponential backoff;

\- invalid payload => failure/dead-letter;

\- gateway performs single transport attempt;

\- durable worker owns retries.



Primary files:

\- `OutboxMessage.cs`

\- `OutboxProcessorWorker.cs`

\- `PostgresOutboxRepository.cs`

\- `IOutboxRepository.cs`

\- `N8nWorkflowGateway.cs`

\- `IWorkflowGateway.cs`



\---



\## Sprint 14 — PDF artifact correctness



Work:

\- active offerings only;

\- include ImageUrl/location scope/location IDs in payload;

\- Failed state on enqueue failure;

\- stale Generating reconciliation;

\- reliable invalidation;

\- remove obsolete combined/legacy semantics where still present.



Primary files:

\- `CatalogGenerationService.cs`

\- `CatalogArtifact.cs`

\- artifact repository

\- hash service

\- Catalog/Services controllers



\---



\## Sprint 15 — License + capabilities



Problem:

invalid/expired license may receive implicit fallback access; capabilities are not enforced consistently.



Work:

\- explicit license validity/free-tier policy;

\- align seeder capabilities with entitlement matrix;

\- enforce capability per mutating/read endpoint.



Primary files:

\- `EntitlementService.cs`

\- `SystemCatalogSeeder.cs`

\- business controllers

\- conversations controller



\---



\## Sprint 16 — Human takeover unified



Work:

\- all takeover/release paths through one application service;

\- update Conversation + durable Context + Redis + Notification together logically;

\- real WhatsApp-human messages handled correctly.



Primary files:

\- `HumanHandoffService.cs`

\- `ConversationStateService.cs`

\- `ConversationsController.cs`

\- `NotificationService.cs`



\---



\## Sprint 17 — Dashboard analytics



Problem:

dashboard rebuilds metrics from operational data.



Work:

\- incremental metrics;

\- no N+1 conversation/message reads;

\- dashboard respects entitlements.



Primary files:

\- dashboard feature/controller

\- new analytics service/repository if needed



\---



\## Sprint 18 — Members



Problem:

MembersController is incomplete and Requests assignment requires valid workspace users.



Work:

\- functional member read model;

\- remove stub behavior;

\- support assignment validation.



\---



\## Sprint 19 — Durable tenant deletion



Problem:

Task.Run + Firestore/PostgreSQL deletion is non-recoverable.



Work:

\- Deleting lifecycle;

\- durable resumable purge;

\- remove related Inbox/Outbox/data safely;

\- eliminate critical Task.Run.



\---



\## Sprint 20 — Repository performance



Work:

\- cursor pagination;

\- remove broad collection reads where practical;

\- workspace-safe message lookup;

\- cheaper location resolution.



\---



\## Sprint 21 — Background worker cleanup



Work:

\- bounded concurrency;

\- graceful shutdown;

\- no business-critical fire-and-forget work.



\---



\## Sprint 22 — Multi-tenant finalization



Work:

\- resolve SuperAdmin vs TenantIsolation behavior;

\- audit impersonation;

\- ensure every query/action remains workspace-scoped.



\---



\## Sprint 23 — Error contracts and observability



Work:

\- consistent error response;

\- correlation IDs;

\- dependency-unavailable distinction;

\- meaningful readiness checks.



\---



\## Sprint 24 — Backend cleanup



After all previous sprints:



Remove confirmed unused/legacy code only after reference search.



Known candidates:

\- legacy Firestore Outbox implementation;

\- old intent enum;

\- deterministic formatter if unused;

\- old reservation conversation enum;

\- duplicate/unused service category DTO;

\- unused workflow gateway methods.



Remove sprint/audit-history comments.



Do not delete anything based only on filename.



\---



\## Sprint 25 — Functional audit



Do not add features.



Verify manually/code-review flows:



\- FAQ/location/hours;

\- product queries;

\- service queries;

\- reservation create/edit/cancel;

\- order multi-turn;

\- request;

\- AI -> human -> AI;

\- human from physical WhatsApp;

\- Redis loss;

\- application restart;

\- duplicate inbound;

\- concurrent messages;

\- Evolution failure;

\- Firestore failure;

\- PostgreSQL failure;

\- n8n failure.



Only after this sprint is backend considered feature-complete enough to move to automated tests and production configuration.

