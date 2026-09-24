import { axiosClient } from '../../../core/api/axiosClient';
import type { CatalogCategoryDto, ProductDto } from '../types/catalog.types';

export const getCategories = async (scope?: 'PRODUCT' | 'SERVICE' | 'SHARED'): Promise<CatalogCategoryDto[]> => {
  const params = scope ? { scope } : {};
  const { data } = await axiosClient.get<CatalogCategoryDto[]>('/catalog/categories', { params });
  return data;
};

export const saveCategory = async (category: CatalogCategoryDto): Promise<CatalogCategoryDto> => {
  if (category.id) {
    const { data } = await axiosClient.put<CatalogCategoryDto>(`/catalog/categories/${category.id}`, category);
    return data;
  }
  const { data } = await axiosClient.post<CatalogCategoryDto>('/catalog/categories', category);
  return data;
};

export const getProducts = async (locationId?: string): Promise<ProductDto[]> => {
  const params = locationId && locationId !== 'all' ? { locationId } : {};
  const { data } = await axiosClient.get<ProductDto[]>('/catalog', { params });
  return data;
};

export const saveProduct = async (product: ProductDto): Promise<ProductDto> => {
  const { data } = await axiosClient.post<ProductDto>('/catalog', product);
  return data;
};

export const deleteProduct = async (id: string): Promise<void> => {
  await axiosClient.delete(`/catalog/${id}`);
};

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