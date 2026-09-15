import { axiosClient } from '../../../core/api/axiosClient';
import type { FaqDto } from '../types/business.types';

export const faqService = {
  // 🔥 SPRINT 09: Añadimos locationId para aislar la data por sede
  getFaqs: async (locationId?: string): Promise<FaqDto[]> => {
    const params = locationId && locationId !== 'all' && locationId !== 'global' ? { locationId } : {};
    const { data } = await axiosClient.get<FaqDto[]>('/business/faqs', { params });
    return data;
  },

  saveFaq: async (faq: FaqDto): Promise<FaqDto> => {
    if (faq.id) {
      const { data } = await axiosClient.put<FaqDto>(`/business/faqs/${faq.id}`, faq);
      return data;
    } else {
      const { data } = await axiosClient.post<FaqDto>('/business/faqs', faq);
      return data;
    }
  },

  deleteFaq: async (id: string): Promise<void> => {
    await axiosClient.delete(`/business/faqs/${id}`);
  }
};