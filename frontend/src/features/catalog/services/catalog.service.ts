import { axiosClient } from '../../../core/api/axiosClient';
import type { ProductDto } from '../types/catalog.types';

export const getProducts = async (): Promise<ProductDto[]> => {
  const { data } = await axiosClient.get<ProductDto[]>('/catalog');
  return data;
};

export const saveProduct = async (product: ProductDto): Promise<void> => {
  await axiosClient.post('/catalog', product);
};

export const deleteProduct = async (id: string): Promise<void> => {
  await axiosClient.delete(`/catalog/${id}`);
};