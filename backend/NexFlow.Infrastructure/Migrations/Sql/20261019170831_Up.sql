START TRANSACTION;
DROP INDEX "IX_Reservations_WorkspaceId_LocationId_ServiceId_StartTime";

CREATE TABLE "ReservationResourceStates" (
    "WorkspaceId" uuid NOT NULL,
    "Kind" character varying(16) NOT NULL,
    "ResourceId" text NOT NULL,
    "Version" bigint NOT NULL,
    "PendingOperationId" uuid,
    "PayloadJson" jsonb,
    "NextAttemptAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReservationResourceStates" PRIMARY KEY ("WorkspaceId", "Kind", "ResourceId"),
    CONSTRAINT "FK_ReservationResourceStates_Workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_Reservations_WorkspaceId_LocationId_ServiceId_StartTime" ON "Reservations" ("WorkspaceId", "LocationId", "ServiceId", "StartTime") WHERE "Status" <> 'Cancelled';

CREATE INDEX "IX_ReservationResourceStates_NextAttemptAt" ON "ReservationResourceStates" ("NextAttemptAt") WHERE "PendingOperationId" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261019170831_CoordinateReservationResourcesAndReuseCancelledSlots', '10.0.11');

COMMIT;
