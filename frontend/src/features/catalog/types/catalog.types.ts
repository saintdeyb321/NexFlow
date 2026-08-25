export interface ProductDto {
  id?: string;
  name: string;
  description: string;
  category: string; // 🔥 NUEVO: Requerido por el backend
  price: number;
  currency: string; // 🔥 NUEVO: Requerido por el backend (Ej: 'PEN', 'USD')
  isActive: boolean;
}