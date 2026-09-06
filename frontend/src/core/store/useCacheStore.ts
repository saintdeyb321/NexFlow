// /core/store/useCacheStore.ts
// 🔥 SPRINT 18 y 19: Hemos eliminado el caché manual de Zustand.
// Toda la gestión de estado del servidor se delega a TanStack Query (useQuery).
// Este archivo ahora solo expone un helper para limpiar el caché cuando el usuario hace logout.

import { QueryClient } from '@tanstack/react-query';

export const clearAllServerCache = (queryClient: QueryClient) => {
  // Purga toda la data del servidor en la memoria para que no sobreviva a los cambios de sesión
  queryClient.clear(); 
};