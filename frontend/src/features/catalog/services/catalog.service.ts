import { axiosClient } from '../../../core/api/axiosClient';
import type { CatalogCategoryDto, CatalogItemDto } from '../types/catalog.types';

// --- CATEGORÍAS (Usadas por Productos y Servicios) ---
export const getCategories = async (): Promise<CatalogCategoryDto[]> => {
  const { data } = await axiosClient.get<CatalogCategoryDto[]>('/catalog/categories');
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

// --- PRODUCTOS (Módulo Catalog) ---
export const getProducts = async (): Promise<CatalogItemDto[]> => {
  const { data } = await axiosClient.get<CatalogItemDto[]>('/catalog');
  return data;
};

export const saveProduct = async (product: CatalogItemDto): Promise<void> => {
  await axiosClient.post('/catalog', product);
};

export const deleteProduct = async (id: string): Promise<void> => {
  await axiosClient.delete(`/catalog/${id}`);
};

export interface ArtifactStatusDto {
  status: 'NOT_GENERATED' | 'GENERATING' | 'CURRENT' | 'STALE' | 'FAILED';
  pdfUrl?: string | null;
  lastGeneratedAt?: string | null;
}

// 🔥 CORRECCIÓN: Agregamos scope obligatorio
export const getArtifactStatus = async (scope: 'PRODUCT' | 'SERVICE'): Promise<ArtifactStatusDto> => {
  const { data } = await axiosClient.get<ArtifactStatusDto>(`/catalog/artifact?scope=${scope}`);
  return data;
};

// 🔥 CORRECCIÓN: Enviamos el scope en el body
export const generateArtifact = async (scope: 'PRODUCT' | 'SERVICE'): Promise<{ status: string, message: string }> => {
  const { data } = await axiosClient.post('/catalog/artifact/generate', { scope });
  return data;
};