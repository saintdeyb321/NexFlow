import { create } from 'zustand';
import { getServices, getLocations } from '../../features/business/services/business.service';
import { faqService } from '../../features/business/services/faq.service';
import type { ServiceDto, LocationDto, FaqDto } from '../../features/business/types/business.types';
import { useAuthStore } from './useAuthStore'; 

interface CacheState {
  workspaceId: string | null;
  services: ServiceDto[] | null;
  locations: LocationDto[] | null;
  faqs: FaqDto[] | null;
  
  isServicesLoading: boolean;
  isLocationsLoading: boolean;
  isFaqsLoading: boolean;

  fetchServices: (force?: boolean) => Promise<void>;
  fetchLocations: (force?: boolean) => Promise<void>;
  fetchFaqs: (force?: boolean) => Promise<void>;

  // 🔥 Añadidos los setters para que las páginas puedan hacer actualizaciones optimistas
  setServices: (services: ServiceDto[]) => void;
  setLocations: (locations: LocationDto[]) => void;
  setFaqs: (faqs: FaqDto[]) => void;
  
  invalidateAll: () => void;
}

export const useCacheStore = create<CacheState>((set, get) => ({
  workspaceId: null,
  services: null,
  locations: null,
  faqs: null,
  
  isServicesLoading: false,
  isLocationsLoading: false,
  isFaqsLoading: false,

  fetchServices: async (force = false) => {
    const currentWorkspaceId = useAuthStore.getState().me?.workspace?.id || null;
    
    if (get().workspaceId !== currentWorkspaceId) {
      set({ workspaceId: currentWorkspaceId, services: null, locations: null, faqs: null });
    } else if (get().services !== null && !force) {
      return; 
    }
    
    if (!currentWorkspaceId) return;

    set({ isServicesLoading: true });
    try {
      const data = await getServices();
      set({ services: data || [] });
    } catch (error) {
      console.error("Error cargando servicios al caché", error);
    } finally {
      set({ isServicesLoading: false });
    }
  },

  fetchLocations: async (force = false) => {
    const currentWorkspaceId = useAuthStore.getState().me?.workspace?.id || null;
    
    if (get().workspaceId !== currentWorkspaceId) {
      set({ workspaceId: currentWorkspaceId, services: null, locations: null, faqs: null });
    } else if (get().locations !== null && !force) {
      return; 
    }
    
    if (!currentWorkspaceId) return;

    set({ isLocationsLoading: true });
    try {
      const data = await getLocations();
      set({ locations: data || [] });
    } catch (error) {
      console.error("Error cargando sedes al caché", error);
    } finally {
      set({ isLocationsLoading: false });
    }
  },

  fetchFaqs: async (force = false) => {
    const currentWorkspaceId = useAuthStore.getState().me?.workspace?.id || null;

    if (get().workspaceId !== currentWorkspaceId) {
      set({ workspaceId: currentWorkspaceId, services: null, locations: null, faqs: null });
    } else if (get().faqs !== null && !force) {
      return;
    }
    
    if (!currentWorkspaceId) return;

    set({ isFaqsLoading: true });
    try {
      const data = await faqService.getFaqs();
      set({ faqs: data || [] });
    } catch (error) {
      console.error("Error cargando FAQs al caché", error);
    } finally {
      set({ isFaqsLoading: false });
    }
  },

  setServices: (services) => set({ services }),
  setLocations: (locations) => set({ locations }),
  setFaqs: (faqs) => set({ faqs }),
  
  invalidateAll: () => set({ workspaceId: null, services: null, locations: null, faqs: null })
}));