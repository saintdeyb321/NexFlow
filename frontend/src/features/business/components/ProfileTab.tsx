import { useState, useEffect } from 'react';
import { Pencil, X } from 'lucide-react';
import { getBusinessProfile, updateBusinessProfile } from '../services/business.service';
import type { BusinessProfile } from '../types/business.types';

export const ProfileTab = ({ showMessage }: { showMessage: (msg: string, type: 'success'|'error') => void }) => {
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isEditing, setIsEditing] = useState(false); // 🔥 NUEVO: Candado de seguridad
  
  const [profile, setProfile] = useState<BusinessProfile>({
    commercialName: '', taxId: '', contactEmail: '', whatsAppNumber: '', description: ''
  });

  const [originalProfile, setOriginalProfile] = useState<BusinessProfile | null>(null);

  useEffect(() => { loadProfile(); }, []);

  const loadProfile = async () => {
    try {
      const data = await getBusinessProfile();
      setProfile(data);
      setOriginalProfile(data); // Guardamos la copia de seguridad
    } catch (error) {
      console.error('Error al cargar perfil', error);
    } finally {
      setIsLoading(false);
    }
  };

  const handleCancel = () => {
    if (originalProfile) setProfile(originalProfile); // Restauramos si cancela
    setIsEditing(false);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    try {
      await updateBusinessProfile(profile);
      setOriginalProfile(profile);
      setIsEditing(false); // Bloqueamos los inputs de nuevo al terminar
      showMessage('Perfil actualizado correctamente', 'success');
    } catch (error) {
      showMessage('Error al guardar el perfil', 'error');
    } finally {
      setIsSaving(false);
    }
  };

  const inputClass = isEditing 
    ? "w-full border rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-blue-500 bg-white" 
    : "w-full border border-transparent rounded-lg px-3 py-2 text-sm outline-none bg-gray-50 text-gray-700 cursor-not-allowed";

  if (isLoading) return <div className="p-8 text-center text-gray-500">Cargando perfil...</div>;

  return (
    <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden relative">
      <div className="px-6 py-4 border-b border-gray-200 bg-gray-50 flex justify-between items-center">
        <h2 className="text-lg font-bold text-gray-800">Información General</h2>
        {!isEditing && (
          <button onClick={() => setIsEditing(true)} className="flex items-center text-sm text-blue-600 hover:text-blue-800 font-medium bg-blue-50 hover:bg-blue-100 px-3 py-1.5 rounded-lg transition-colors">
            <Pencil className="w-4 h-4 mr-2" /> Editar Perfil
          </button>
        )}
      </div>
      
      <form onSubmit={handleSubmit} className="p-6 space-y-5">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Nombre Comercial *</label>
            <input type="text" disabled={!isEditing} value={profile.commercialName} onChange={e => setProfile({...profile, commercialName: e.target.value})} required className={inputClass} />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">RUC / Identificador Fiscal *</label>
            <input type="text" disabled={!isEditing} value={profile.taxId} onChange={e => setProfile({...profile, taxId: e.target.value})} required className={inputClass} />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Correo de Contacto *</label>
            <input type="email" disabled={!isEditing} value={profile.contactEmail} onChange={e => setProfile({...profile, contactEmail: e.target.value})} required className={inputClass} />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">WhatsApp *</label>
            <input type="text" disabled={!isEditing} value={profile.whatsAppNumber} onChange={e => setProfile({...profile, whatsAppNumber: e.target.value})} required className={inputClass} />
          </div>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Descripción del Negocio</label>
          <textarea disabled={!isEditing} value={profile.description} onChange={e => setProfile({...profile, description: e.target.value})} rows={3} className={inputClass} />
        </div>

        {isEditing && (
          <div className="flex justify-end pt-4 gap-3 border-t mt-4">
            <button type="button" onClick={handleCancel} className="px-5 py-2 flex items-center bg-gray-100 text-gray-600 text-sm font-medium rounded-lg hover:bg-gray-200 transition-colors">
              <X className="w-4 h-4 mr-1" /> Cancelar
            </button>
            <button type="submit" disabled={isSaving} className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:opacity-50 transition-colors shadow-sm">
              {isSaving ? 'Guardando...' : 'Guardar Cambios'}
            </button>
          </div>
        )}
      </form>
    </div>
  );
};