export type ArtifactScope = 'PRODUCT' | 'SERVICE';

export const visualStyleOptions = [
  { value: 'GASTRONOMY', label: 'Gastronómico' },
  { value: 'CLINICAL', label: 'Clínico' },
  { value: 'LEGAL', label: 'Legal' },
  { value: 'HOSPITALITY', label: 'Hospitalidad' },
  { value: 'BEAUTY', label: 'Belleza' },
  { value: 'MODERN', label: 'Moderno' },
] as const;

export const paletteOptions = [
  { value: 'CLINICAL_BLUE', label: 'Azul clínico', ink: '#0B3565', accent: '#0B72D9', background: '#E8F2FC' },
  { value: 'WARM_SUNSET', label: 'Atardecer cálido', ink: '#6B2D22', accent: '#C45B2A', background: '#FFF3E4' },
  { value: 'PREMIUM_DARK', label: 'Oscuro premium', ink: '#F4EEE1', accent: '#D8B665', background: '#171923' },
  { value: 'EMERALD', label: 'Esmeralda', ink: '#064E3B', accent: '#087F5B', background: '#ECFDF5' },
  { value: 'LAVENDER', label: 'Lavanda', ink: '#44325E', accent: '#8061B0', background: '#F6F0FF' },
  { value: 'OCEAN', label: 'Océano', ink: '#083344', accent: '#087F96', background: '#ECFEFF' },
] as const;

export const creativityOptions = [
  { value: 'FORMAL', label: 'Sobrio' },
  { value: 'BALANCED', label: 'Balanceado' },
  { value: 'CREATIVE', label: 'Creativo' },
] as const;

export interface ArtifactDesign {
  visualStyle: typeof visualStyleOptions[number]['value'];
  palette: typeof paletteOptions[number]['value'];
  creativity: typeof creativityOptions[number]['value'];
}

export interface ArtifactStatusDto {
  status: 'NOT_GENERATED' | 'GENERATING' | 'CURRENT' | 'STALE' | 'FAILED';
  pdfUrl: string | null;
  lastGeneratedAt: string | null;
  origin: 'GENERATED' | 'UPLOADED';
  visualStyle: ArtifactDesign['visualStyle'] | null;
  palette: ArtifactDesign['palette'] | null;
  creativity: ArtifactDesign['creativity'] | null;
}

export interface GenerateArtifactRequest {
  scope: ArtifactScope;
  design: ArtifactDesign;
  replaceCurrent: boolean;
}

export interface GenerateArtifactResponse {
  status: ArtifactStatusDto['status'];
  message: string;
  sourceHash: string;
}

export interface UploadArtifactRequest {
  scope: ArtifactScope;
  replaceCurrent: boolean;
  file: File;
}

export const MAX_PDF_BYTES = 15 * 1024 * 1024;
