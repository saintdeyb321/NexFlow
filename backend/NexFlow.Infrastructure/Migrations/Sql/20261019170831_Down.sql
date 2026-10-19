START TRANSACTION;
LOCK TABLE "Reservations", "ReservationResourceStates" IN ACCESS EXCLUSIVE MODE;
DO $guard$
BEGIN
    IF EXISTS (SELECT 1 FROM "ReservationResourceStates") THEN
        RAISE EXCEPTION 'Rollback refused: durable reservation resource epochs exist. Keep the compatible schema.' USING ERRCODE = '55000';
    END IF;
    IF EXISTS (
        SELECT 1 FROM "Reservations"
        GROUP BY "WorkspaceId", "LocationId", "ServiceId", "StartTime" HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Rollback refused: reused reservation slots are incompatible with the old unique index.' USING ERRCODE = '55000';
    END IF;
END;
$guard$;

DROP TABLE "ReservationResourceStates";

DROP INDEX "IX_Reservations_WorkspaceId_LocationId_ServiceId_StartTime";

CREATE UNIQUE INDEX "IX_Reservations_WorkspaceId_LocationId_ServiceId_StartTime" ON "Reservations" ("WorkspaceId", "LocationId", "ServiceId", "StartTime");

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261019170831_CoordinateReservationResourcesAndReuseCancelledSlots';

COMMIT;
