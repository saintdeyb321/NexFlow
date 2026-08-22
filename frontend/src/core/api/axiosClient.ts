import axios from 'axios';
import { auth } from '../../app/config/firebase';
import { useAuthStore } from '../store/useAuthStore'; // Importamos el store

const baseURL = import.meta.env.VITE_API_URL;

export const axiosClient = axios.create({
  baseURL,
  headers: {
    'Content-Type': 'application/json',
  },
});

axiosClient.interceptors.request.use(
  async (config) => {
    // 1. Inyección del Token de Firebase
    const user = auth.currentUser;
    if (user) {
      const token = await user.getIdToken();
      config.headers.Authorization = `Bearer ${token}`;
    }

    // 2. INYECCIÓN DEL TENANT (Workspace ID)
    // Extraemos el estado actual fuera del árbol de React de forma segura
    const workspaceId = useAuthStore.getState().me?.workspace?.id;
    if (workspaceId) {
      config.headers['X-Workspace-Id'] = workspaceId;
    }

    return config;
  },
  (error) => Promise.reject(error)
);