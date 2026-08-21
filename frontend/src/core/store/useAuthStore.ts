import { create } from 'zustand';
import { axiosClient } from '../api/axiosClient';
import type { MeResponse } from '../types/auth.types';
import { auth } from '../../app/config/firebase';
import { signOut, onAuthStateChanged } from 'firebase/auth'; 
import type { User} from 'firebase/auth';

interface AuthState {
  isAuthenticated: boolean;
  isLoading: boolean;
  me: MeResponse | null;
  
  // Acciones
  checkSession: () => Promise<void>;
  logout: () => Promise<void>;
}

export const useAuthStore = create<AuthState>((set) => ({
  isAuthenticated: false,
  isLoading: true, // Empieza cargando para evitar destellos de pantalla
  me: null,

  checkSession: async () => {
    set({ isLoading: true });
    try {
      // Esperamos a que Firebase confirme si hay sesión local
      await new Promise<User | null>((resolve) => {
        // FIX 2 y 3: Uso modular de Firebase y tipado explícito del parámetro "user"
        const unsubscribe = onAuthStateChanged(auth, (user: User | null) => {
          unsubscribe();
          resolve(user);
        });
      });

      if (!auth.currentUser) {
        set({ isAuthenticated: false, me: null, isLoading: false });
        return;
      }

      // Si hay sesión en Firebase, traemos la verdad absoluta del Backend
      const { data } = await axiosClient.get<MeResponse>('/me');
      
      set({ 
        isAuthenticated: true, 
        me: data, 
        isLoading: false 
      });

    } catch (error) {
      console.error("Error validando sesión contra el backend:", error);
      // Si el backend lo rechaza (ej. usuario suspendido), lo deslogueamos localmente
      await signOut(auth);
      set({ isAuthenticated: false, me: null, isLoading: false });
    }
  },

  logout: async () => {
    await signOut(auth);
    set({ isAuthenticated: false, me: null, isLoading: false });
  }
}));