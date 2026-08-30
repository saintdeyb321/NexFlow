import { create } from 'zustand';
// Ajusta esta ruta a donde tengas tu archivo business.service.ts
import { getServices, getLocations } from '../../features/business/services/business.service';
import type { ServiceDto, LocationDto } from '../../features/business/types/business.types';

interface BusinessState {
  services: ServiceDto[];
  locations: LocationDto[];
  lastFetched: number;
  fetchData: (forceRefresh?: boolean) => Promise<void>;
  clearCache: () => void;
}

const CACHE_TTL_MS = 1000 * 60 * 5; 

export const useBusinessStore = create<BusinessState>((set, get) => ({
  services: [],
  locations: [],
  lastFetched: 0,
  
  fetchData: async (forceRefresh = false) => {
    const now = Date.now();
    const { lastFetched } = get();

    // Si no forzamos refresco y los datos aún están "frescos", abortamos la petición HTTP
    if (!forceRefresh && (now - lastFetched < CACHE_TTL_MS)) {
      console.log('⚡ Sirviendo Servicios y Sedes desde la caché de Zustand');
      return; 
    }

    try {
      const [servicesData, locationsData] = await Promise.all([
        getServices(),
        getLocations()
      ]);

      set({ 
        services: servicesData, 
        locations: locationsData, 
        lastFetched: now 
      });
    } catch (error) {
      console.error("Error actualizando la caché de negocio:", error);
    }
  },

  clearCache: () => set({ services: [], locations: [], lastFetched: 0 })
}));