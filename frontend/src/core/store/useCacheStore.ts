import { create } from 'zustand';
import { getServices } from '../../features/business/services/business.service';
import { faqService } from '../../features/business/services/faq.service';
import type { ServiceDto } from '../../features/business/types/business.types';
import type { FaqDto } from '../../features/business/types/business.types';
// 🔥 SPRINT 2 (Auditoría #31): Importamos la sesión para aislar los datos
import { useAuthStore } from './useAuthStore'; 

interface CacheState {
  workspaceId: string | null;
  services: ServiceDto[] | null;
  faqs: FaqDto[] | null;
  isServicesLoading: boolean;
  isFaqsLoading: boolean;

  fetchServices: (force?: boolean) => Promise<void>;
  fetchFaqs: (force?: boolean) => Promise<void>;

  setServices: (services: ServiceDto[]) => void;
  setFaqs: (faqs: FaqDto[]) => void;
  
  invalidateAll: () => void;
}

export const useCacheStore = create<CacheState>((set, get) => ({
  workspaceId: null,
  services: null,
  faqs: null,
  isServicesLoading: false,
  isFaqsLoading: false,

  fetchServices: async (force = false) => {
    const currentWorkspaceId = useAuthStore.getState().me?.workspace?.id || null;
    
    // 🔥 SPRINT 2 (Auditoría #31): Si cambió el Workspace, vaciamos la memoria (Prevención de data bleed)
    if (get().workspaceId !== currentWorkspaceId) {
      set({ workspaceId: currentWorkspaceId, services: null, faqs: null });
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

  fetchFaqs: async (force = false) => {
    const currentWorkspaceId = useAuthStore.getState().me?.workspace?.id || null;

    if (get().workspaceId !== currentWorkspaceId) {
      set({ workspaceId: currentWorkspaceId, services: null, faqs: null });
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
  setFaqs: (faqs) => set({ faqs }),
  
  invalidateAll: () => set({ workspaceId: null, services: null, faqs: null })
}));