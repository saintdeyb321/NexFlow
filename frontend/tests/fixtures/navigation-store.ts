import { create } from 'zustand';
import type { MeResponse } from '../../src/core/types/auth.types';

const modules = ['BUSINESS_PROFILE', 'LOCATIONS', 'CONVERSATIONS', 'RESERVATIONS', 'ORDERS', 'REQUESTS', 'SERVICES', 'CATALOG', 'FAQ'];
export const navigationIdentity: MeResponse = {
  user: { id: 'test-user', email: 'test@example.invalid', firstName: 'Ana', lastName: 'Pérez', isSuperAdmin: false },
  workspace: { id: 'workspace-a', name: 'Nombre interno del workspace', status: 'Active' },
  license: null, membershipRole: 'Owner', entitlements: modules,
  capabilities: Object.fromEntries(modules.map(module => [module, ['READ', 'UPDATE']])),
};
export const useAuthStore = create<{
  me: MeResponse | null; selectedLocationId: string; setSelectedLocationId: (id: string) => void; logout: () => Promise<void>;
}>(set => ({
  me: structuredClone(navigationIdentity), selectedLocationId: 'all', setSelectedLocationId: id => set({ selectedLocationId: id }),
  logout: async () => { set({ me: null, selectedLocationId: 'all' }); },
}));
