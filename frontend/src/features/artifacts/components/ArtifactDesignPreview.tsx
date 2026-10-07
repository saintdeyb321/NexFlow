import { creativityOptions, paletteOptions, visualStyleOptions } from '../types/artifact.types';
import type { ArtifactDesign, ArtifactScope } from '../types/artifact.types';

export const ArtifactDesignPreview = ({ design, scope }: { design: ArtifactDesign; scope: ArtifactScope }) => {
  const palette = paletteOptions.find(option => option.value === design.palette) ?? paletteOptions[0];
  const styleLabel = visualStyleOptions.find(option => option.value === design.visualStyle)?.label;
  const creativityLabel = creativityOptions.find(option => option.value === design.creativity)?.label;
  const serif = ['GASTRONOMY', 'LEGAL', 'HOSPITALITY'].includes(design.visualStyle);
  return (
    <figure aria-label={`Previsualización: ${styleLabel}, ${palette.label}, ${creativityLabel}`} className="space-y-2">
      <figcaption className="text-sm font-medium text-foreground">Vista de estilo</figcaption>
      <div className={`relative overflow-hidden border p-5 ${design.visualStyle === 'CLINICAL' || design.visualStyle === 'LEGAL' ? 'rounded-md' : 'rounded-2xl'}`}
        style={{ backgroundColor: palette.background, color: palette.ink, borderColor: palette.accent }}>
        {design.creativity === 'CREATIVE' && <div aria-hidden="true" className="absolute -right-5 -top-8 h-28 w-28 rounded-full opacity-20" style={{ backgroundColor: palette.accent }} />}
        <div className={`relative ${design.creativity === 'FORMAL' ? 'text-center' : ''}`}>
          <p className="text-xs uppercase tracking-widest">{scope === 'PRODUCT' ? 'Productos' : 'Servicios'}</p>
          <p className={`mt-2 text-2xl font-semibold ${design.visualStyle === 'BEAUTY' ? 'italic' : ''}`}
            style={{ fontFamily: serif ? 'Georgia, serif' : undefined }}>Tu negocio</p>
          <div className="my-4 h-1 w-12 rounded-full" style={{ backgroundColor: palette.accent, marginInline: design.creativity === 'FORMAL' ? 'auto' : undefined }} />
          <div className={`grid gap-3 ${design.creativity === 'FORMAL' ? 'grid-cols-1' : 'grid-cols-2'}`}>
            {[1, 2].map(item => <div key={item} className="rounded-lg border p-3 text-left" style={{ borderColor: palette.accent }}>
              <div aria-hidden="true" className="mb-2 h-8 rounded opacity-20" style={{ backgroundColor: palette.accent }} />
              <p className="text-xs font-semibold">{scope === 'PRODUCT' ? 'Nombre del producto' : 'Nombre del servicio'}</p>
              <p className="mt-1 text-[11px]">Descripción · precio · moneda</p>
            </div>)}
          </div>
          <span className="mt-4 inline-block rounded-lg px-3 py-1.5 text-xs font-medium" style={{ backgroundColor: palette.ink, color: palette.background }}>Conoce más</span>
        </div>
      </div>
      <p className="text-xs text-muted">La composición es ilustrativa. Los datos reales de tu negocio se mantienen.</p>
    </figure>
  );
};
