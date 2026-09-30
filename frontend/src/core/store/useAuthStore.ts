import { create } from 'zustand';
import { axiosClient, ApiError, setActiveWorkspaceId } from '../api/axiosClient'; 
import type { MeResponse } from '../types/auth.types';
import { auth } from '../../app/config/firebase';
import { signOut, onAuthStateChanged } from 'firebase/auth'; 
import type { User } from 'firebase/auth';
import { clearNexFlowStorage, connectQueryIdentity, disconnectQueryIdentity } from '../query/queryPersistence';

interface AuthState {
  isAuthenticated: boolean;
  isLoading: boolean;
  isBootstrapping: boolean;
  me: MeResponse | null;
  
  selectedLocationId: string | 'all';
  setSelectedLocationId: (id: string | 'all') => void;
  
  checkSession: (identityChanged?: boolean) => Promise<void>;
  logout: () => Promise<void>;
}

let sessionEpoch = 0;
let checkingEpoch: number | null = null;
let firebaseIdentity: string | null = null;

export const useAuthStore = create<AuthState>((set, get) => ({
  isAuthenticated: false,
  isLoading: true, 
  isBootstrapping: true,
  me: null,
  
  selectedLocationId: 'all',
  setSelectedLocationId: (id) => set({ selectedLocationId: id }),

  checkSession: async (identityChanged = false) => {
    if (checkingEpoch !== null && !identityChanged) return;
    const attempt = ++sessionEpoch;
    checkingEpoch = attempt;

    if (identityChanged) {
      disconnectQueryIdentity();
      firebaseIdentity = null;
      setActiveWorkspaceId(null);
      set({ me: null, isAuthenticated: false, isBootstrapping: true, selectedLocationId: 'all' });
    }
    
    set({ isLoading: true });
    
    try {
      const firebaseUser = await new Promise<User | null>((resolve) => {
        const unsubscribe = onAuthStateChanged(auth, (user: User | null) => {
          unsubscribe();
          resolve(user);
        });
      });

      if (attempt !== sessionEpoch) return;
      if (!firebaseUser) {
        disconnectQueryIdentity();
        clearNexFlowStorage();
        firebaseIdentity = null;
        setActiveWorkspaceId(null); 
        set({ isAuthenticated: false, me: null, isLoading: false, isBootstrapping: false, selectedLocationId: 'all' });
        return;
      }

      if (firebaseIdentity !== firebaseUser.uid) {
        disconnectQueryIdentity();
        setActiveWorkspaceId(null);
        set({ me: null, isAuthenticated: false, isBootstrapping: true, selectedLocationId: 'all' });
      }

      const { data } = await axiosClient.get<MeResponse>('/me', { headers: { 'Cache-Control': 'no-cache' } });
      if (attempt !== sessionEpoch || auth.currentUser?.uid !== firebaseUser.uid) return;
      const workspaceChanged = get().me?.workspace?.id !== data.workspace?.id;
      connectQueryIdentity(data);
      firebaseIdentity = firebaseUser.uid;
      
      setActiveWorkspaceId(data.workspace?.id || null); 
      set({ isAuthenticated: true, me: data, isLoading: false, isBootstrapping: false,
        ...(workspaceChanged ? { selectedLocationId: 'all' } : {}) });

    } catch (error: unknown) {
      if (attempt !== sessionEpoch) return;
      // 🔥 SPRINT 01: Diferenciar errores de red vs errores de autorización reales
      if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
        await get().logout();
      } else {
        // Mantenemos la sesión si el backend está caído (5xx o Error de Red)
        set({ isLoading: false, isBootstrapping: false });
      }
    } finally {
      if (checkingEpoch === attempt) checkingEpoch = null;
    }
  },

  logout: async () => {
    sessionEpoch++;
    checkingEpoch = null;
    firebaseIdentity = null;
    disconnectQueryIdentity();
    setActiveWorkspaceId(null);
    clearNexFlowStorage();
    set({ isAuthenticated: false, me: null, isLoading: false, isBootstrapping: false, selectedLocationId: 'all' });
    try {
      await signOut(auth);
    } catch (e) {
      console.error("Error signing out:", e);
    }
  }
}));
