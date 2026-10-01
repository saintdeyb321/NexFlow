import { FirebaseError } from 'firebase/app';
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

    } catch (err: unknown) {
      const code = err instanceof FirebaseError ? err.code : undefined;
      setError(code === 'auth/popup-closed-by-user' || code === 'auth/cancelled-popup-request'
        ? 'Se cerró la ventana de Google. Intenta iniciar sesión nuevamente.'
        : code === 'auth/popup-blocked'
          ? 'Permite las ventanas emergentes para continuar con Google.'
          : code === 'auth/network-request-failed'
            ? 'No se pudo conectar. Revisa tu conexión e intenta nuevamente.'
            : 'No pudimos iniciar sesión con Google. Intenta nuevamente.');
    } finally {
      setIsLoading(false);
    }
  };

  return { login, isLoading, error };
};