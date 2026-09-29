import { useState, useRef } from 'react';
import { UploadCloud, Loader2, X, AlertCircle } from 'lucide-react';
import { axiosClient } from '../../core/api/axiosClient';

interface ImageUploaderProps {
  value?: string | null;
  onChange: (url: string | null) => void;
  onUploadingContext?: (isUploading: boolean) => void;
  label?: string;
}

const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

export const ImageUploader = ({ value, onChange, onUploadingContext, label = "Imagen" }: ImageUploaderProps) => {
  const [isUploading, setIsUploading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    setErrorMsg(null);

    // 🔥 SPRINT 08: Validación estricta sin alert()
    if (file.size > 5 * 1024 * 1024) {
      setErrorMsg("La imagen supera los 5MB permitidos.");
      return;
    }
    if (!ALLOWED_TYPES.includes(file.type)) {
      setErrorMsg("Solo se permiten archivos JPG, PNG y WebP.");
      return;
    }

    setIsUploading(true);
    if (onUploadingContext) onUploadingContext(true);

    const formData = new FormData();
    formData.append('file', file);

    try {
      const { data } = await axiosClient.post<{ secureUrl: string }>('/storage/upload', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      onChange(data.secureUrl);
    } catch (error: any) {
      setErrorMsg("Ocurrió un error de red al subir la imagen.");
    } finally {
      setIsUploading(false);
      if (onUploadingContext) onUploadingContext(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  };

  return (
    <div className="w-full">
      <label className="block text-sm font-medium text-gray-700 mb-1">{label}</label>
      
      {errorMsg && (
        <div className="mb-2 text-xs font-medium text-red-600 flex items-center bg-red-50 p-2 rounded-lg border border-red-100">
          <AlertCircle className="w-3 h-3 mr-1" /> {errorMsg}
        </div>
      )}

      {value ? (
        <div className="relative w-full h-40 bg-gray-100 rounded-xl border border-gray-200 overflow-hidden group">
          <img src={value} alt="Preview" className="w-full h-full object-cover" />
          <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center">
            <button 
              type="button" 
              onClick={() => { onChange(null); setErrorMsg(null); }} 
              className="p-2 bg-red-500 text-white rounded-full hover:bg-red-600 shadow-lg transform transition-transform hover:scale-110"
              title="Eliminar imagen"
            >
              <X className="w-5 h-5" />
            </button>
          </div>
        </div>
      ) : (
        <button
          type="button"
          onClick={() => fileInputRef.current?.click()}
          disabled={isUploading}
          className="w-full h-40 flex flex-col items-center justify-center bg-gray-50 border-2 border-dashed border-gray-300 rounded-xl hover:bg-blue-50 hover:border-blue-400 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {isUploading ? (
            <>
              <Loader2 className="w-8 h-8 text-blue-500 animate-spin mb-2" />
              <span className="text-sm text-gray-500 font-medium">Subiendo de forma segura...</span>
            </>
          ) : (
            <>
              <UploadCloud className="w-8 h-8 text-gray-400 mb-2" />
              <span className="text-sm font-medium text-gray-700">Haz clic para subir imagen</span>
              <span className="text-xs text-gray-500 mt-1">PNG, JPG, WebP hasta 5MB</span>
            </>
          )}
        </button>
      )}

      <input 
        type="file" 
        ref={fileInputRef} 
        onChange={handleFileChange} 
        accept={ALLOWED_TYPES.join(',')} 
        className="hidden" 
      />
    </div>
  );
};