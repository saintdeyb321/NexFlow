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

// 🔥 SPRINT 01: Utilidad central para estandarizar mensajes de error en la UI y evitar leer error.response
export const getApiErrorPresentation = (error: unknown): string => {
  if (error instanceof ApiError) {
    if (error.status === 401) return "Tu sesión ha expirado.";
    if (error.status === 403) return "No tienes acceso a esta función.";
    if (error.status === 404) return "El recurso solicitado no existe.";
    if (error.status === 409) return error.message; 
    if (error.status === 429) return "Límite de peticiones alcanzado. Intenta más tarde.";
    if (error.status >= 500) return "Ocurrió un problema inesperado en el servidor.";
    if (error.status === 0) return "Sin conexión al servidor. Verifica tu internet.";
    return error.message;
  }
  return "Ocurrió un error inesperado.";
};

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
      const finalCorrelationId = data?.correlationId || error.response.headers?.['x-correlation-id'];

      let message = 'Error desconocido en el servidor';
      if (typeof data === 'string' && data.trim() !== '') {
        message = data;
      } else if (data?.message || data?.detail) {
        message = data.message || data.detail;
      }

      const code = data?.code || data?.title || 'UNKNOWN_ERROR';

      // 🔥 SPRINT 01: Despachar evento de expiración para evitar apps congeladas
      if (status === 401) {
        window.dispatchEvent(new CustomEvent('session-expired'));
      }

      return Promise.reject(new ApiError(status, code, message, finalCorrelationId));
    } else if (error.request) {
      return Promise.reject(new ApiError(0, 'NETWORK_ERROR', 'Sin conexión al servidor'));
    }
    return Promise.reject(error);
  }
);