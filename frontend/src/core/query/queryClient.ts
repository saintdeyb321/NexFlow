import { QueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/axiosClient';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      refetchIntervalInBackground: false,
      staleTime: 10 * 1000,
      gcTime: 30 * 60 * 1000,
      retry: (failureCount, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failureCount < 1,
    },
    mutations: { retry: false },
  },
});
