import { axiosClient } from '../../../core/api/axiosClient';
import type { ProductCategoryDto, ProductDto } from '../types/catalog.types';

export const getCategories = async (scope: 'PRODUCT' | 'SHARED' = 'PRODUCT'): Promise<ProductCategoryDto[]> => {
  const { data } = await axiosClient.get<ProductCategoryDto[]>('/catalog/categories', { params: { scope } });
  return data;
};

export const saveCategory = async (category: ProductCategoryDto): Promise<ProductCategoryDto> => {
  if (category.id) {
    const { data } = await axiosClient.put<ProductCategoryDto>(`/catalog/categories/${category.id}`, category);
    return data;
  }
  const { data } = await axiosClient.post<ProductCategoryDto>('/catalog/categories', category);
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