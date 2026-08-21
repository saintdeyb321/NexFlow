import { Navigate } from 'react-router-dom';
import { useGoogleLogin } from '../hooks/useGoogleLogin';
import { useAuthStore } from '../../core/store/useAuthStore';

export const LoginPage = () => {
  const { login, isLoading, error } = useGoogleLogin();
  const { isAuthenticated } = useAuthStore();

  if (isAuthenticated) {
    return <Navigate to="/" replace />;
  }

  return (
    <div className="min-h-screen flex flex-col items-center justify-center bg-white">
      <div className="w-full max-w-sm px-8 flex flex-col items-center">
        {/* Logo Minimalista */}
        <div className="mb-10 text-center">
          <h1 className="text-3xl font-bold tracking-tighter text-gray-900">NexFlow</h1>
          <p className="text-sm text-gray-500 mt-2">Plataforma de Automatización SaaS</p>
        </div>

        {/* Mensaje de Error (si ocurre) */}
        {error && (
          <div className="w-full mb-6 p-3 text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg text-center">
            {error}
          </div>
        )}

        {/* Botón de Google Estándar */}
        <button
          onClick={login}
          disabled={isLoading}
          className="w-full flex items-center justify-center gap-3 bg-white text-gray-700 font-medium py-2.5 px-4 border border-gray-300 rounded-md hover:bg-gray-50 hover:shadow-sm focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-200 transition-all disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <svg className="w-5 h-5" viewBox="0 0 48 48">
            <path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"/>
            <path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"/>
            <path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"/>
            <path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"/>
          </svg>
          {isLoading ? 'Conectando...' : 'Continuar con Google'}
        </button>
      </div>
    </div>
  );
};