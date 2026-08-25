import { axiosClient } from '../../../core/api/axiosClient';
import type { FaqDto } from '../types/business.types'; // Ajusta la importación según tu estructura

export const faqService = {
  getFaqs: async (): Promise<FaqDto[]> => {
    const { data } = await axiosClient.get<FaqDto[]>('/business/faqs');
    return data;
  },

  saveFaq: async (faq: Partial<FaqDto>): Promise<FaqDto> => {
    const { data } = await axiosClient.post<FaqDto>('/business/faqs', faq);
    return data;
  },

  deleteFaq: async (id: string): Promise<void> => {
    await axiosClient.delete(`/business/faqs/${id}`);
  }
};