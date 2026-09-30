import { useSyncExternalStore } from 'react';
import type { QueryKey } from '@tanstack/react-query';

export const STABLE_TTL = 20 * 60 * 1000;
export const queryPolicies = {
  stable: { staleTime: STABLE_TTL, gcTime: 60 * 60 * 1000 },
  dynamic: { staleTime: 10 * 1000 },
  inbox: { staleTime: 5 * 1000 },
};

// Only complete stable-resource keys qualify for persistence.
export const stableQueryModule = (key: QueryKey): string | null => {
  if (key[0] !== 'workspace' || typeof key[1] !== 'string') return null;
  if (key.length === 4 && key[2] === 'business' && key[3] === 'profile') return 'BUSINESS_PROFILE';
  if (key.length === 3 && key[2] === 'locations') return 'LOCATIONS';
  if (key.length === 4 && key[2] === 'hours' && typeof key[3] === 'string' && key[3] !== 'all') return 'BUSINESS_HOURS';
  if (key.length === 3 && key[2] === 'faqs') return 'FAQ';
  if (key.length === 4 && key[2] === 'services' && (key[3] === null || typeof key[3] === 'string')) return 'SERVICES';
  if (key.length === 5 && key[2] === 'catalog') {
    if (key[3] === 'products' && (key[4] === null || typeof key[4] === 'string')) return 'CATALOG';
    if (key[3] === 'categories') {
      if (key[4] === 'PRODUCT') return 'CATALOG';
      if (key[4] === 'SERVICE') return 'SERVICES';
      if (key[4] === 'SHARED') return 'SHARED';
    }
  }
  return null;
};

const subscribeVisibility = (listener: () => void) => {
  document.addEventListener('visibilitychange', listener);
  return () => document.removeEventListener('visibilitychange', listener);
};
export const usePageVisible = () => useSyncExternalStore(subscribeVisibility,
  () => document.visibilityState === 'visible', () => false);
