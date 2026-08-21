export interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
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

// Este es el espejo exacto de tu MeDto de C#
export interface MeResponse {
  user: User;
  workspace: Workspace | null;
  license: License | null;
  entitlements: string[]; 
}