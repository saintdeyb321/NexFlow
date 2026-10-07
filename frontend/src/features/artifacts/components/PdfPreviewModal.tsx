import { Copy, ExternalLink } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { Modal } from '../../../components/ui/Modal';

interface PdfPreviewModalProps {
  pdfUrl: string;
  title: string;
  onClose: () => void;
  onCopy: () => void;
}

export const PdfPreviewModal = ({ pdfUrl, title, onClose, onCopy }: PdfPreviewModalProps) => (
  <Modal isOpen title={title} size="xl" onClose={onClose}>
    <div className="mb-4 flex flex-wrap gap-2">
      <a href={pdfUrl} target="_blank" rel="noopener noreferrer" className="nf-button nf-button-secondary">
        <ExternalLink aria-hidden="true" className="h-4 w-4" /> Abrir en nueva pestaña
      </a>
      <Button variant="secondary" onClick={onCopy}><Copy aria-hidden="true" className="h-4 w-4" /> Copiar enlace</Button>
    </div>
    <iframe src={pdfUrl} title={`Previsualización de ${title}`} className="h-[60dvh] min-h-64 w-full rounded-lg border border-line" />
    <p className="mt-3 text-xs text-muted">Si tu navegador no muestra el PDF, ábrelo en una nueva pestaña.</p>
  </Modal>
);
