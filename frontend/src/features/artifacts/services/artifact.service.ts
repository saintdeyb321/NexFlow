import { axiosClient } from '../../../core/api/axiosClient';

export interface ArtifactStatusDto {
  status: 'NOT_GENERATED' | 'GENERATING' | 'CURRENT' | 'STALE' | 'FAILED';
  pdfUrl?: string | null;
  lastGeneratedAt?: string | null;
}

export const getArtifactStatus = async (scope: 'PRODUCT' | 'SERVICE'): Promise<ArtifactStatusDto> => {
  const { data } = await axiosClient.get<ArtifactStatusDto>(`/catalog/artifact?scope=${scope}`);
  return data;
};

export const generateArtifact = async (scope: 'PRODUCT' | 'SERVICE'): Promise<{ status: string, message: string }> => {
  const { data } = await axiosClient.post('/catalog/artifact/generate', { scope });
  return data;
};