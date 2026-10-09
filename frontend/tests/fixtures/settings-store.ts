import { create } from 'zustand';
import type { MeResponse } from '../../src/core/types/auth.types';

export const settingsIdentity: MeResponse = {
  user: { id: 'test-user', email: 'test@example.invalid', firstName: 'Ana', lastName: 'Pérez', isSuperAdmin: false },
  workspace: { id: 'workspace-a', name: 'Negocio ficticio', status: 'Active' }, license: null, membershipRole: 'Owner',
  entitlements: ['BUSINESS_PROFILE', 'LOCATIONS', 'BUSINESS_HOURS', 'CONVERSATIONS'],
  capabilities: { BUSINESS_PROFILE: ['READ', 'UPDATE'], LOCATIONS: ['READ', 'CREATE', 'UPDATE', 'DELETE'], BUSINESS_HOURS: ['READ', 'UPDATE'], CONVERSATIONS: ['READ', 'CONFIGURE'] },
};
export const useAuthStore = create<{ me: MeResponse | null; selectedLocationId: string; setSelectedLocationId: (id: string) => void; logout: () => Promise<void> }>(set => ({
  me: structuredClone(settingsIdentity), selectedLocationId: 'all', setSelectedLocationId: id => set({ selectedLocationId: id }), logout: async () => set({ me: null, selectedLocationId: 'all' }),
}));
