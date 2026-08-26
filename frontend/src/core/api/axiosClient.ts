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
  // Fallo #4 de la auditoría: Asegurarnos de usar HTTPS si el backend lo fuerza, o usar la variable de entorno
  baseURL: import.meta.env.VITE_API_URL || 'https://localhost:7182/api',
  headers: {
    'Content-Type': 'application/json',
  },
});

// Función inyectora para evitar dependencias circulares con Zustand
export const setWorkspaceHeader = (workspaceId: string | null) => {
  if (workspaceId) {
    axiosClient.defaults.headers.common['X-Workspace-Id'] = workspaceId;
  } else {
    delete axiosClient.defaults.headers.common['X-Workspace-Id'];
  }
};

// Interceptor solo para el Token de Google
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

// 🔥 CORRECCIÓN (Fallo #20): Interceptor silencioso y estructurado.
axiosClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response) {
      const status = error.response.status;
      const data = error.response.data;
      const message = data?.message || data?.error || 'Error desconocido en el servidor';
      const code = data?.code || 'UNKNOWN_ERROR';

      // Logueamos silenciosamente para debugging, sin interrumpir la UI
      if (status === 401) console.warn("⛔ [401] Sesión expirada o inválida");
      else if (status === 403) console.warn(`🔒 [403] Acceso Denegado: ${message}`);
      else if (status === 404) console.warn(`🔍 [404] Endpoint no encontrado: ${error.config.url}`);
      else if (status >= 500) console.error(`🔥 [500] Error del Servidor Backend`);

      return Promise.reject(new ApiError(status, code, message));
    } else if (error.request) {
      return Promise.reject(new ApiError(0, 'NETWORK_ERROR', 'Sin conexión al servidor'));
    }
    return Promise.reject(error);
  }
);