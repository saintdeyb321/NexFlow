import axios from 'axios';
import { auth } from '../../app/config/firebase';

export class ApiError extends Error {
  status: number;
  code: string;

  constructor(status: number, code: string, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
  }
}

export const axiosClient = axios.create({
  baseURL: import.meta.env.VITE_API_URL || 'http://localhost:5068/api',
  headers: {
    'Content-Type': 'application/json',
  },
});

// 🔥 BUENA PRÁCTICA: Función inyectora para evitar dependencias circulares con Zustand
export const setWorkspaceHeader = (workspaceId: string | null) => {
  if (workspaceId) {
    axiosClient.defaults.headers.common['X-Workspace-Id'] = workspaceId;
  } else {
    delete axiosClient.defaults.headers.common['X-Workspace-Id'];
  }
};

// Interceptor solo para el Token de Google (Auth siempre está disponible)
axiosClient.interceptors.request.use(
  async (config) => {
    const user = auth.currentUser;
    if (user) {
      const token = await user.getIdToken();
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// Interceptor de Errores Global (ApiError)
axiosClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response) {
      const status = error.response.status;
      const data = error.response.data;
      const message = data?.message || data?.error || 'Error desconocido en el servidor';
      const code = data?.code || 'UNKNOWN_ERROR';

      if (status === 401) console.error("⛔ [401] Sesión expirada o inválida");
      else if (status === 403) alert(`🔒 Acceso Denegado: Licencia o permisos insuficientes.\nDetalle: ${message}`);
      else if (status === 404) console.error(`🔍 [404] Endpoint no encontrado: ${error.config.url}`);
      else if (status >= 500) alert(`🔥 Error del Servidor (500): Algo falló en el backend.`);

      return Promise.reject(new ApiError(status, code, message));
    } else if (error.request) {
      alert("🌐 Error de red: No se pudo conectar con el servidor.");
      return Promise.reject(new ApiError(0, 'NETWORK_ERROR', 'Sin conexión al servidor'));
    }
    return Promise.reject(error);
  }
);