import axios from 'axios';
import { auth } from '../../app/config/firebase';

export class ApiError extends Error {
  status: number;
  code: string;
  correlationId?: string;

  constructor(status: number, code: string, message: string, correlationId?: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.correlationId = correlationId;
  }
}

export const axiosClient = axios.create({
  // 🔥 SPRINT 8: Cero localhosts hardcodeados, entorno estricto
  baseURL: import.meta.env.VITE_API_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

export const setWorkspaceHeader = (workspaceId: string | null) => {
  if (workspaceId) {
    axiosClient.defaults.headers.common['X-Workspace-Id'] = workspaceId;
  } else {
    delete axiosClient.defaults.headers.common['X-Workspace-Id'];
  }
};

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

axiosClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response) {
      const status = error.response.status;
      const data = error.response.data;

      const message = data?.message || data?.detail || 'Error desconocido en el servidor';
      const code = data?.code || data?.title || 'UNKNOWN_ERROR';
      const correlationId = data?.correlationId;

      if (status === 401) console.warn("⛔ [401] Sesión expirada o inválida");
      else if (status === 403) console.warn(`🔒 [403] Acceso Denegado: ${message}`);
      else if (status === 404) console.warn(`🔍 [404] Endpoint no encontrado: ${error.config.url}`);
      else if (status >= 500) console.error(`🔥 [500] Error del Servidor Backend: ${message}`);

      return Promise.reject(new ApiError(status, code, message, correlationId));
    } else if (error.request) {
      return Promise.reject(new ApiError(0, 'NETWORK_ERROR', 'Sin conexión al servidor'));
    }
    return Promise.reject(error);
  }
);