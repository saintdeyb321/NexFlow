import type { QueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../../core/query/queryKeys.ts';

export const invalidateReservationViews = (client: QueryClient, workspaceId: string, locationId: string) =>
  Promise.all([
    client.invalidateQueries({ queryKey: queryKeys.reservations.lists(workspaceId) }),
    client.invalidateQueries({ queryKey: queryKeys.reservations.availabilityByLocation(workspaceId, locationId) }),
  ]);

// A profile write can change civil boundaries and slots as well as the metadata.
export const invalidateReservationContext = (client: QueryClient, workspaceId: string) =>
  Promise.all([
    client.invalidateQueries({ queryKey: queryKeys.reservations.context(workspaceId), exact: true }),
    client.invalidateQueries({ queryKey: queryKeys.reservations.lists(workspaceId) }),
    client.invalidateQueries({ queryKey: queryKeys.reservations.availability(workspaceId) }),
  ]);
