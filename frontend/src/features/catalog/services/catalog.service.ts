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