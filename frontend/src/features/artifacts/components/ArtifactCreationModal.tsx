import { useState } from 'react';
import { FileText, Sparkles, Upload } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { Modal } from '../../../components/ui/Modal';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { Input, FormField } from '../../../components/ui/Form';
import { ArtifactDesignPreview } from './ArtifactDesignPreview';
import { MAX_PDF_BYTES, creativityOptions, paletteOptions, visualStyleOptions } from '../types/artifact.types';
import type { ArtifactDesign, ArtifactScope, ArtifactStatusDto } from '../types/artifact.types';

export type ArtifactCreationMode = 'generate' | 'upload';

interface ArtifactCreationModalProps {
  scope: ArtifactScope;
  artifact?: ArtifactStatusDto;
  initialMode: ArtifactCreationMode;
  isSubmitting: boolean;
  blocked: boolean;
  onClose: () => void;
  onGenerate: (design: ArtifactDesign, replaceCurrent: boolean) => void;
  onUpload: (file: File, replaceCurrent: boolean) => void;
}

export const ArtifactCreationModal = ({ scope, artifact, initialMode, isSubmitting, blocked, onClose, onGenerate, onUpload }: ArtifactCreationModalProps) => {
  const [mode, setMode] = useState<ArtifactCreationMode>(initialMode);
  const [design, setDesign] = useState<ArtifactDesign>(() => ({
    visualStyle: artifact?.visualStyle ?? 'MODERN',
    palette: artifact?.palette ?? 'CLINICAL_BLUE',
    creativity: artifact?.creativity ?? 'BALANCED',
  }));
  const [file, setFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [confirmMode, setConfirmMode] = useState<ArtifactCreationMode | null>(null);
  const requiresReplacement = Boolean(artifact?.pdfUrl) || artifact?.status === 'CURRENT' || artifact?.status === 'STALE';
  const disabled = isSubmitting || blocked;

  const chooseFile = (candidate?: File) => {
    setFile(null);
    setFileError(null);
    if (!candidate) return;
    if (!candidate.name.toLowerCase().endsWith('.pdf') || candidate.type !== 'application/pdf') {
      setFileError('Selecciona un archivo .pdf.');
      return;
    }
    if (candidate.size < 5 || candidate.size > MAX_PDF_BYTES) {
      setFileError('El PDF debe pesar como máximo 15 MB y no estar vacío.');
      return;
    }
    setFile(candidate);
  };

  const submit = (submissionMode: ArtifactCreationMode, replaceCurrent: boolean) => {
    if (disabled) return;
    if (submissionMode === 'generate') onGenerate(design, replaceCurrent);
    else if (file) onUpload(file, replaceCurrent);
    setConfirmMode(null);
  };

  const requestSubmission = (event: React.FormEvent) => {
    event.preventDefault();
    if (disabled || (mode === 'upload' && !file)) return;
    if (requiresReplacement) setConfirmMode(mode);
    else submit(mode, false);
  };

  return (
    <Modal isOpen title={`Crear folleto de ${scope === 'PRODUCT' ? 'productos' : 'servicios'}`} size="xl" onClose={onClose} closeDisabled={isSubmitting}>
      <div role="group" aria-label="Cómo crear el folleto" className="mb-5 flex flex-wrap gap-2">
        <Button variant={mode === 'generate' ? 'primary' : 'secondary'} aria-pressed={mode === 'generate'} disabled={disabled} onClick={() => setMode('generate')}>
          <Sparkles aria-hidden="true" className="h-4 w-4" /> Generar con NexFlow
        </Button>
        <Button variant={mode === 'upload' ? 'primary' : 'secondary'} aria-pressed={mode === 'upload'} disabled={disabled} onClick={() => setMode('upload')}>
          <Upload aria-hidden="true" className="h-4 w-4" /> Subir mi PDF
        </Button>
      </div>
      {blocked && <p role="status" className="mb-4 text-sm text-muted">No puedes crear otra versión mientras se está generando tu folleto o si ya no tienes permiso.</p>}
      <form onSubmit={requestSubmission} className="space-y-5">
        <fieldset disabled={disabled} className="space-y-5">
          {mode === 'generate' ? <>
            <fieldset>
              <legend className="mb-2 text-sm font-medium text-foreground">Estilo visual</legend>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                {visualStyleOptions.map(option => <label key={option.value} className={`flex cursor-pointer items-center gap-2 rounded-xl border p-3 text-sm ${design.visualStyle === option.value ? 'border-primary bg-primary/5' : 'border-line'}`}>
                  <Input type="radio" name="visualStyle" value={option.value} checked={design.visualStyle === option.value} onChange={() => setDesign({ ...design, visualStyle: option.value })} />
                  {option.label}
                </label>)}
              </div>
            </fieldset>
            <fieldset>
              <legend className="mb-2 text-sm font-medium text-foreground">Paleta</legend>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                {paletteOptions.map(option => <label key={option.value} className={`cursor-pointer rounded-xl border p-3 ${design.palette === option.value ? 'border-primary bg-primary/5' : 'border-line'}`}>
                  <span className="mb-2 flex items-center gap-2 text-sm"><Input type="radio" name="palette" value={option.value} checked={design.palette === option.value} onChange={() => setDesign({ ...design, palette: option.value })} />{option.label}</span>
                  <span aria-hidden="true" className="flex overflow-hidden rounded-lg border border-line">
                    {[option.ink, option.accent, option.background].map(color => <span key={color} className="h-6 flex-1" style={{ backgroundColor: color }} />)}
                  </span>
                </label>)}
              </div>
            </fieldset>
            <fieldset>
              <legend className="mb-2 text-sm font-medium text-foreground">Creatividad</legend>
              <div className="flex flex-wrap gap-3">
                {creativityOptions.map(option => <label key={option.value} className="flex cursor-pointer items-center gap-2 text-sm">
                  <Input type="radio" name="creativity" value={option.value} checked={design.creativity === option.value} onChange={() => setDesign({ ...design, creativity: option.value })} />{option.label}
                </label>)}
              </div>
            </fieldset>
            <ArtifactDesignPreview design={design} scope={scope} />
          </> : <>
            <FormField label="Tu PDF" helperText="Solo .pdf, máximo 15 MB. Subirlo no consume tu cuota de generación." error={fileError}>
              <Input type="file" accept=".pdf" onChange={event => chooseFile(event.target.files?.[0])} />
            </FormField>
            {file && <div className="flex items-start gap-3 rounded-xl border border-line p-4">
              <FileText aria-hidden="true" className="h-5 w-5 shrink-0 text-primary" />
              <div className="min-w-0"><p className="break-all text-sm font-medium">{file.name}</p><p className="mt-1 text-xs text-muted">{(file.size / (1024 * 1024)).toFixed(2)} MB · {file.size.toLocaleString()} bytes</p></div>
            </div>}
          </>}
        </fieldset>
        <div className="flex flex-wrap justify-end gap-2 border-t border-line pt-4">
          <Button variant="secondary" disabled={isSubmitting} onClick={onClose}>Cancelar</Button>
          <Button type="submit" isLoading={isSubmitting} disabled={blocked || (mode === 'upload' && !file)}>
            {mode === 'generate' ? 'Generar folleto' : 'Subir PDF'}
          </Button>
        </div>
      </form>
      <ConfirmDialog isOpen={confirmMode !== null} title={confirmMode === 'upload' ? '¿Reemplazar tu folleto?' : '¿Crear una nueva versión?'}
        description={confirmMode === 'upload' ? 'El PDF seleccionado reemplazará tu folleto cuando se guarde correctamente.' : 'La versión anterior seguirá disponible mientras creamos la nueva. Se reemplazará cuando termine la generación.'}
        confirmLabel={confirmMode === 'upload' ? 'Sí, reemplazar PDF' : 'Sí, crear nueva versión'} isLoading={isSubmitting} confirmDisabled={blocked}
        onClose={() => setConfirmMode(null)} onConfirm={() => { if (confirmMode) submit(confirmMode, true); }} />
    </Modal>
  );
};
