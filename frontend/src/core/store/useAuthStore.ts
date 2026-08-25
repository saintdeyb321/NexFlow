import { create } from 'zustand';
import { axiosClient, setWorkspaceHeader } from '../api/axiosClient'; // 🔥 Importamos el inyector
import type { MeResponse } from '../types/auth.types';
import { auth } from '../../app/config/firebase';
import { signOut, onAuthStateChanged } from 'firebase/auth'; 
import type { User } from 'firebase/auth';

interface AuthState {
  isAuthenticated: boolean;
  isLoading: boolean;
  me: MeResponse | null;
  
  checkSession: () => Promise<void>;
  logout: () => Promise<void>;
  completeOnboarding: () => Promise<void>;
}

export const useAuthStore = create<AuthState>((set) => ({
  isAuthenticated: false,
  isLoading: true, 
  me: null,

  checkSession: async () => {
    set({ isLoading: true });
    try {
      await new Promise<User | null>((resolve) => {
        const unsubscribe = onAuthStateChanged(auth, (user: User | null) => {
          unsubscribe();
          resolve(user);
        });
      });

      if (!auth.currentUser) {
        setWorkspaceHeader(null); // Limpiamos Axios
        set({ isAuthenticated: false, me: null, isLoading: false });
        return;
      }

      const { data } = await axiosClient.get<MeResponse>('/me');
      
      setWorkspaceHeader(data.workspace?.id || null); // 🔥 Inyectamos el Tenant a Axios
      set({ isAuthenticated: true, me: data, isLoading: false });

    } catch (error: any) {
      console.error("Error validando sesión contra el backend:", error);
      await signOut(auth);
      setWorkspaceHeader(null);
      set({ isAuthenticated: false, me: null, isLoading: false });
      
      if (error.response?.status === 401 || error.response?.status === 403) {
          alert("⛔ Acceso denegado: Tu cuenta de Google no está registrada o no tienes permisos.");
      }
    }
  },

  logout: async () => {
    await signOut(auth);
    setWorkspaceHeader(null);
    set({ isAuthenticated: false, me: null, isLoading: false });
  },

  completeOnboarding: async () => {
    try {
      const { data } = await axiosClient.get<MeResponse>('/me');
      setWorkspaceHeader(data.workspace?.id || null);
      set({ me: data });
    } catch (error) {
      console.error("Error al sincronizar la sesión post-onboarding:", error);
    }
  }
}));