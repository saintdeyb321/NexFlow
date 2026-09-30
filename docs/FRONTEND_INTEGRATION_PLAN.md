\# NexFlow Frontend Integration \& UX Plan



Status: ACTIVE  

Backend hardening baseline: CLOSED  

Starting frontend phase: PHASE 1



\## Goal



Bring NexFlow frontend to functional parity with the hardened backend and then improve performance, maintainability, UX and visual quality without weakening backend security or multi-tenant guarantees.



The backend remains the source of truth for:

\- business rules;

\- permissions;

\- entitlements;

\- capabilities;

\- lifecycle transitions;

\- validation;

\- persistence.



The frontend must faithfully represent those rules.



\---



\# PHASE 1 — API Contract \& Security Parity



Goal: make backend and frontend contracts 1:1 before performance or redesign work.



Scope:



\- `/me` role/capabilities contract.

\- Product create vs update.

\- Service create vs update.

\- Category create/update/delete.

\- FAQ create vs update.

\- Reservation DTO synchronization.

\- Request assignment using real UserId.

\- Members/assignees integration.

\- Request lifecycle UI.

\- Order nullable fields and lifecycle UI.

\- Conversation Pending/Attempting/Sent/Failed/UnknownDelivery.

\- Dashboard nullable metrics.

\- Workspace `Deleting`.

\- Capability-aware actions.

\- Storage upload authorization.

\- Settings/WhatsApp capability visibility.

\- TypeScript/API error contract cleanup in touched files.



Exit condition:



Backend and frontend build successfully and no known contract/security mismatch remains for the affected modules.



\---



\# PHASE 2 — Cache, Queries \& Performance



Goal: reduce unnecessary API/Firestore reads and prevent redundant rerenders/refetches.



Architecture:



TanStack Query owns server state.



Zustand owns only client/UI state.



Scope:



\- query-key factory;

\- workspace/user-scoped keys;

\- staleTime/gcTime policy by data volatility;

\- persisted cache for safe/stable data;

\- cache schema version;

\- clear/isolate cache on logout/workspace change;

\- targeted invalidation after mutations;

\- remove duplicate manual fetch/cache logic;

\- eliminate redundant queries;

\- optimize polling;

\- incremental/cursor chat loading if backend support is required;

\- prevent repeated full message reloads;

\- avoid duplicate catalog/category queries.



Stable data may use longer TTL:

\- profile;

\- locations;

\- hours;

\- FAQ;

\- products;

\- services;

\- categories;

\- members;

\- entitlements.



Dynamic data uses shorter TTL:

\- dashboard;

\- requests;

\- orders;

\- reservations;

\- notifications;

\- conversations.



Exit condition:



Refresh/navigation no longer causes unnecessary full reloads and tenant cache isolation is preserved.



\---



\# PHASE 3 — Shared UI Architecture



Goal: remove duplicated UX logic and create reusable primitives.



Create/reuse shared components for:



\- Button

\- IconButton

\- Modal/Dialog

\- ConfirmDialog

\- Toast

\- Alert

\- Badge/StatusBadge

\- Card

\- Input

\- Select

\- Textarea

\- FormField

\- PageHeader

\- EmptyState

\- ErrorState

\- LoadingState

\- Skeleton

\- Drawer

\- DataTable where appropriate



Requirements:



\- React composition, not inheritance.

\- accessible modal/dialog behavior;

\- Escape handling;

\- focus restoration/trapping;

\- consistent form errors;

\- centralized notifications;

\- replace `alert`, `confirm`, `prompt`;

\- shared API-error presentation;

\- production ErrorBoundary must not expose technical exceptions.



Exit condition:



Pages consume shared primitives instead of implementing their own modal/error/notification patterns.



\---



\# PHASE 4 — UX/UI Redesign



Goal: make NexFlow professional, coherent, responsive and pleasant without changing product behavior.



Introduce design tokens for:



\- primary;

\- secondary;

\- accent;

\- success;

\- warning;

\- danger;

\- surfaces;

\- borders;

\- text;

\- muted text.



Improve:



\- app shell/sidebar/header;

\- login;

\- dashboard;

\- inbox/conversations;

\- products;

\- services;

\- reservations;

\- requests;

\- orders;

\- settings;

\- notifications;

\- superadmin;

\- responsive/mobile behavior;

\- empty/loading/error states;

\- forms and validation feedback;

\- tables/cards;

\- action discoverability;

\- touch/mobile controls;

\- accessibility and keyboard navigation.



Do not introduce decorative complexity that harms usability.



Exit condition:



All major screens follow the same visual system and are usable on desktop and mobile.



\---



\# PHASE 5 — Final Frontend \& Integration Certification



Goal: certify the complete web application against the hardened backend.



Verify:



\- frontend build;

\- backend build if integration changes occurred;

\- API contract parity;

\- role/capability matrix;

\- module entitlements;

\- tenant isolation;

\- cache isolation;

\- logout/workspace switching;

\- CRUD workflows;

\- lifecycle workflows;

\- conversations/handoff;

\- Evolution delivery states;

\- reservations;

\- orders;

\- requests;

\- settings;

\- SuperAdmin deletion lifecycle;

\- responsive behavior;

\- accessibility basics;

\- loading/error/empty states;

\- no technical error leakage;

\- no production `alert/confirm/prompt`;

\- no known unsafe `any` in critical contracts;

\- no unnecessary direct business Firestore access.



Final state:



FRONTEND INTEGRATION CLOSED



Only after Phase 5 passes.



\---



\## Workflow



Each phase is implemented independently.



After each implementation:



Developer commits and pushes.



External audit reviews the real commit/diff.



The next phase begins only after the previous phase is approved.



Do not combine phases unless explicitly instructed.

