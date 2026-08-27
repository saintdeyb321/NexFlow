import { create } from 'zustand';
import { getServices } from '../../features/business/services/business.service';
import { faqService } from '../../features/business/services/faq.service';
import type { ServiceDto } from '../../features/business/types/business.types';
import type { FaqDto } from '../../features/business/types/business.types';

interface CacheState {
  services: ServiceDto[] | null;
  faqs: FaqDto[] | null;
  isServicesLoading: boolean;
  isFaqsLoading: boolean;

  fetchServices: (force?: boolean) => Promise<void>;
  fetchFaqs: (force?: boolean) => Promise<void>;

  // Mutaciones optimistas para actualizar la UI sin recargar
  setServices: (services: ServiceDto[]) => void;
  setFaqs: (faqs: FaqDto[]) => void;
  
  invalidateAll: () => void;
}

export const useCacheStore = create<CacheState>((set, get) => ({
  services: null,
  faqs: null,
  isServicesLoading: false,
  isFaqsLoading: false,

  fetchServices: async (force = false) => {
    // Si ya tenemos los datos y no estamos forzando recarga, no hacemos nada (Caché Hit)
    if (get().services !== null && !force) return; 
    
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

  fetchFaqs: async (force = false) => {
    if (get().faqs !== null && !force) return;
    
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
  setFaqs: (faqs) => set({ faqs }),
  
  invalidateAll: () => set({ services: null, faqs: null })
}));