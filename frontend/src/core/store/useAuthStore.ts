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
  
  checkSession: () => Promise<void>;
  logout: () => Promise<void>;
}

let isCheckingSession = false;

export const useAuthStore = create<AuthState>((set) => ({
  isAuthenticated: false,
  isLoading: true, 
  isBootstrapping: true,
  me: null,

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
        set({ isAuthenticated: false, me: null, isLoading: false, isBootstrapping: false });
        isCheckingSession = false;
        return;
      }

      const { data } = await axiosClient.get<MeResponse>('/me');
      
      setActiveWorkspaceId(data.workspace?.id || null); 
      set({ isAuthenticated: true, me: data, isLoading: false, isBootstrapping: false });

    } catch (error: unknown) {
      console.error("Error validando sesión contra el backend:", error);
      await signOut(auth);
      
      setActiveWorkspaceId(null); 
      set({ isAuthenticated: false, me: null, isLoading: false, isBootstrapping: false });
      
      if (error instanceof ApiError) {
        if (error.status === 401 || error.status === 403) {
           console.warn("⛔ Sesión rechazada: Tu cuenta no está registrada o no tienes permisos.");
        }
      }
    } finally {
      isCheckingSession = false;
    }
  },

  logout: async () => {
    await signOut(auth);
    setActiveWorkspaceId(null); 
    set({ isAuthenticated: false, me: null, isLoading: false, isBootstrapping: false });
  }
}));