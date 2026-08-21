export interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  isSuperAdmin: boolean; // <-- AÑADIDO: Autorización dictada por el backend
}

export interface Workspace {
  id: string;
  name: string;
  status: string;
}

export interface License {
  type: string;
  status: string;
  expiresAt: string | null;
}

export interface MeResponse {
  user: User;
  workspace: Workspace | null;
  license: License | null;
  entitlements: string[]; 
}