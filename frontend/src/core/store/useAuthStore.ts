import { create } from 'zustand';
import { axiosClient, ApiError } from '../api/axiosClient'; // Importamos ApiError
import type { MeResponse } from '../types/auth.types';
import { auth } from '../../app/config/firebase';
import { signOut, onAuthStateChanged } from 'firebase/auth'; 
import type { User } from 'firebase/auth';

interface AuthState {
  isAuthenticated: boolean;
  isLoading: boolean;
  isBootstrapping: boolean; // 🔥 NUEVO: Estado explícito para el arranque de la App
  me: MeResponse | null;
  
  checkSession: () => Promise<void>;
  logout: () => Promise<void>;
  completeOnboarding: () => Promise<void>;
}

let isCheckingSession = false; // Candado para evitar doble ejecución

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
        set({ isAuthenticated: false, me: null, isLoading: false, isBootstrapping: false });
        isCheckingSession = false;
        return;
      }

      const { data } = await axiosClient.get<MeResponse>('/me');
      
      set({ isAuthenticated: true, me: data, isLoading: false, isBootstrapping: false });

    } catch (error: unknown) {
      console.error("Error validando sesión contra el backend:", error);
      await signOut(auth);
      set({ isAuthenticated: false, me: null, isLoading: false, isBootstrapping: false });
      
      // 🔥 CORRECCIÓN (Fallo #19): Ahora evaluamos el error correctamente usando ApiError
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
    set({ isAuthenticated: false, me: null, isLoading: false, isBootstrapping: false });
  },

  completeOnboarding: async () => {
    try {
      const { data } = await axiosClient.get<MeResponse>('/me');
      set({ me: data });
    } catch (error) {
      console.error("Error al sincronizar la sesión post-onboarding:", error);
    }
  }
}));