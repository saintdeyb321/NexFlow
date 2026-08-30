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

// 🔥 ARQUITECTURA LIMPIA: Variable en memoria aislada (sin localStorage)
let activeWorkspaceId: string | null = null;

export const setActiveWorkspaceId = (id: string | null) => {
  activeWorkspaceId = id;
};

export const axiosClient = axios.create({
  baseURL: import.meta.env.VITE_API_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

axiosClient.interceptors.request.use(
  async (config) => {
    const user = auth.currentUser;
    if (user) {
      const token = await user.getIdToken();
      config.headers.Authorization = `Bearer ${token}`;
    }
    
    // Inyectamos el ID directo desde la memoria
    if (activeWorkspaceId) {
      config.headers['X-Workspace-Id'] = activeWorkspaceId;
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
      const headerCorrelationId = error.response.headers?.['x-correlation-id'];

      let message = 'Error desconocido en el servidor';
      if (typeof data === 'string' && data.trim() !== '') {
        message = data;
      } else if (data?.message || data?.detail) {
        message = data.message || data.detail;
      }

      const code = data?.code || data?.title || 'UNKNOWN_ERROR';
      const finalCorrelationId = data?.correlationId || headerCorrelationId;

      if (status === 401) console.warn("⛔ [401] Sesión expirada o inválida");
      else if (status === 403) console.warn(`🔒 [403] Acceso Denegado: ${message}`);
      else if (status === 404) console.warn(`🔍 [404] Endpoint no encontrado: ${error.config.url}`);
      else if (status >= 500) console.error(`🔥 [500] Error del Servidor Backend: ${message} (Trace: ${finalCorrelationId})`);

      return Promise.reject(new ApiError(status, code, message, finalCorrelationId));
    } else if (error.request) {
      return Promise.reject(new ApiError(0, 'NETWORK_ERROR', 'Sin conexión al servidor'));
    }
    return Promise.reject(error);
  }
);