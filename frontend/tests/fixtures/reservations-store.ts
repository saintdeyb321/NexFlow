import { create } from 'zustand';
import type { MeResponse } from '../../src/core/types/auth.types';

const me: MeResponse = {
  user: { id: 'test-user', email: 'test@example.invalid', firstName: 'Prueba', lastName: 'Local', isSuperAdmin: false },
  workspace: { id: 'workspace-a', name: 'Negocio de prueba', status: 'Active' },
  license: null, membershipRole: 'Owner',
  entitlements: ['RESERVATIONS', 'BUSINESS_PROFILE', 'SERVICES', 'LOCATIONS'],
  capabilities: { RESERVATIONS: ['READ', 'CREATE', 'UPDATE', 'COMPLETE', 'CANCEL', 'CHECK_AVAILABILITY'], BUSINESS_PROFILE: ['READ'], SERVICES: ['READ'], LOCATIONS: ['READ'] },
};
export const useAuthStore = create<{ me: MeResponse; selectedLocationId: string; setSelectedLocationId: (id: string) => void }>(set => ({
  me, selectedLocationId: 'all', setSelectedLocationId: id => set({ selectedLocationId: id }),
}));
