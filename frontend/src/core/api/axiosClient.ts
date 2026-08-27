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
  baseURL: import.meta.env.VITE_API_URL || 'https://localhost:7182/api',
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

      // 🛡️ CONTRATO UNIFICADO ESTRICTO
      // Busca 'message' prioritariamente. 'detail' queda como red de seguridad de ASP.NET
      const message = data?.message || data?.Message || data?.detail || 'Error desconocido en el servidor';
      const code = data?.code || data?.Error || data?.title || 'UNKNOWN_ERROR';

      if (status === 401) console.warn("⛔ [401] Sesión expirada o inválida");
      else if (status === 403) console.warn(`🔒 [403] Acceso Denegado: ${message}`);
      else if (status === 404) console.warn(`🔍 [404] Endpoint no encontrado: ${error.config.url}`);
      else if (status >= 500) console.error(`🔥 [500] Error del Servidor Backend: ${message}`);

      return Promise.reject(new ApiError(status, code, message));
    } else if (error.request) {
      return Promise.reject(new ApiError(0, 'NETWORK_ERROR', 'Sin conexión al servidor'));
    }
    return Promise.reject(error);
  }
);