import { create } from 'zustand';
import { axiosClient } from '../api/axiosClient';
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
        set({ isAuthenticated: false, me: null, isLoading: false });
        return;
      }

      const { data } = await axiosClient.get<MeResponse>('/me');
      set({ isAuthenticated: true, me: data, isLoading: false });

    } catch (error: any) {
      console.error("Error validando sesión contra el backend:", error);
      await signOut(auth);
      set({ isAuthenticated: false, me: null, isLoading: false });
      
      // 🔥 NUEVO: Mostrar alerta si el backend rechaza al usuario
      if (error.response?.status === 401 || error.response?.status === 403) {
          alert("⛔ Acceso denegado: Tu cuenta de Google no está registrada en el sistema o no tienes permisos para ingresar.");
      }
    }
  },

  logout: async () => {
    await signOut(auth);
    set({ isAuthenticated: false, me: null, isLoading: false });
  },

  completeOnboarding: async () => {
    // 🔥 SPRINT 9: Sincronización Real con Backend
    // Ya no falsificamos el estado. Solicitamos la confirmación oficial del servidor.
    try {
      const { data } = await axiosClient.get<MeResponse>('/me');
      set({ me: data });
    } catch (error) {
      console.error("Error al sincronizar la sesión post-onboarding:", error);
    }
  }
}));