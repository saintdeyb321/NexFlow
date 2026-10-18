import { create } from 'zustand';
import type { MeResponse } from '../../src/core/types/auth.types';

const query = new URLSearchParams(location.search);
const me: MeResponse = {
  user: { id: 'local-user', email: 'local@example.invalid', firstName: 'Prueba', lastName: 'Local', isSuperAdmin: false },
  workspace: { id: 'workspace-a', name: 'Prueba local', status: 'Active' }, license: null, membershipRole: 'Owner',
  entitlements: query.has('withoutModule') ? [] : ['CONVERSATIONS'], capabilities: { CONVERSATIONS: query.has('withoutRead') ? [] : ['READ'] },
};
export const useAuthStore = create(() => ({ me, isAuthenticated: !query.has('anonymous'), isLoading: false, selectedLocationId: 'all' }));
