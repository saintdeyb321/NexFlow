import { create } from 'zustand';
import { axiosClient, ApiError, setActiveWorkspaceId } from '../api/axiosClient'; 
import type { MeResponse } from '../types/auth.types';
import { auth } from '../../app/config/firebase';
import { signOut, onAuthStateChanged } from 'firebase/auth'; 
import type { User } from 'firebase/auth';

interface AuthState {
  isAuthenticated: boolean;
  isLoading: boolean;
  isBootstrapping: boolean;
  me: MeResponse | null;
  
  selectedLocationId: string | 'all';
  setSelectedLocationId: (id: string | 'all') => void;
  
  checkSession: () => Promise<void>;
  logout: () => Promise<void>;
}

let isCheckingSession = false;

export const useAuthStore = create<AuthState>((set) => ({
  isAuthenticated: false,
  isLoading: true, 
  isBootstrapping: true,
  me: null,
  
  selectedLocationId: 'all',
  setSelectedLocationId: (id) => set({ selectedLocationId: id }),

  checkSession: async () => {
    if (isCheckingSession) return;
    isCheckingSession = true;
    
    set({ isLoading: true });
    
    try {
      await new Promise<User | null>((resolve) => {
        const unsubscribe = onAuthStateChanged(auth, (user: User | null) => {
          unsubscribe();
          resolve(user);
        });
      });

      if (!auth.currentUser) {
        setActiveWorkspaceId(null); 
        set({ isAuthenticated: false, me: null, isLoading: false, isBootstrapping: false, selectedLocationId: 'all' });
        isCheckingSession = false;
        return;
      }

      const { data } = await axiosClient.get<MeResponse>('/me');
      
      setActiveWorkspaceId(data.workspace?.id || null); 
      set({ isAuthenticated: true, me: data, isLoading: false, isBootstrapping: false });

    } catch (error: unknown) {
      // 🔥 SPRINT 01: Diferenciar errores de red vs errores de autorización reales
      if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
        await signOut(auth);
        setActiveWorkspaceId(null); 
        set({ isAuthenticated: false, me: null, isLoading: false, isBootstrapping: false, selectedLocationId: 'all' });
      } else {
        // Mantenemos la sesión si el backend está caído (5xx o Error de Red)
        set({ isLoading: false, isBootstrapping: false });
      }
    } finally {
      isCheckingSession = false;
    }
  },

  logout: async () => {
    try {
      await signOut(auth);
    } catch (e) {
      console.error("Error signing out:", e);
    } finally {
      setActiveWorkspaceId(null); 
      
      if (typeof window !== 'undefined') {
        localStorage.clear();
        sessionStorage.clear();
      }

      set({ isAuthenticated: false, me: null, isLoading: false, isBootstrapping: false, selectedLocationId: 'all' });
    }
  }
}));