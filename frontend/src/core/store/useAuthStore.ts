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

    } catch (error) {
      console.error("Error validando sesión contra el backend:", error);
      await signOut(auth);
      set({ isAuthenticated: false, me: null, isLoading: false });
    }
  },

  logout: async () => {
    await signOut(auth);
    set({ isAuthenticated: false, me: null, isLoading: false });
  },

  completeOnboarding: async () => {
    set((state) => {
      if (state.me && state.me.workspace) {
        return {
          me: {
            ...state.me,
            workspace: { ...state.me.workspace, status: 'Active' }
          }
        };
      }
      return state;
    });
  }
}));