import { axiosClient } from '../../../core/api/axiosClient';
import type { ArtifactScope, ArtifactStatusDto, GenerateArtifactRequest, GenerateArtifactResponse, UploadArtifactRequest } from '../types/artifact.types';

export const getArtifactStatus = async (scope: ArtifactScope, signal?: AbortSignal): Promise<ArtifactStatusDto> => {
  const { data } = await axiosClient.get<ArtifactStatusDto>(`/catalog/artifact?scope=${scope}`, { signal });
  return data;
};

export const generateArtifact = async (request: GenerateArtifactRequest): Promise<GenerateArtifactResponse> => {
  const { data } = await axiosClient.post<GenerateArtifactResponse>('/catalog/artifact/generate', request);
  return data;
};

export const uploadArtifact = async ({ scope, replaceCurrent, file }: UploadArtifactRequest): Promise<ArtifactStatusDto> => {
  const form = new FormData();
  form.append('scope', scope);
  form.append('replaceCurrent', String(replaceCurrent));
  form.append('file', file);
  const { data } = await axiosClient.post<ArtifactStatusDto>('/catalog/artifact/upload', form, {
    // Let the browser provide the multipart boundary instead of the JSON default.
    headers: { 'Content-Type': undefined },
  });
  return data;
};
