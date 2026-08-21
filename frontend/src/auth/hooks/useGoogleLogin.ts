import { useState } from 'react';
import { signInWithPopup, GoogleAuthProvider } from 'firebase/auth';
import { auth } from '../../app/config/firebase';
import { useAuthStore } from '../../core/store/useAuthStore';

export const useGoogleLogin = () => {
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  
  // Extraemos la función para sincronizar con tu backend (PostgreSQL/C#)
  const checkSession = useAuthStore((state) => state.checkSession);

  const login = async () => {
    setIsLoading(true);
    setError(null);
    try {
      const provider = new GoogleAuthProvider();
      // 1. Autenticación contra Google/Firebase
      await signInWithPopup(auth, provider);
      
      // 2. Sincronización contra tu Backend (Llama a /api/me)
      await checkSession();
      
    } catch (err: any) {
      console.error(err);
      setError(err.message || 'Error al iniciar sesión con Google');
    } finally {
      setIsLoading(false);
    }
  };

  return { login, isLoading, error };
};