import { useState, useEffect } from 'react';
import { getBusinessProfile, updateBusinessProfile } from '../services/business.service';
import type { BusinessProfile } from '../types/business.types';

export const ProfileTab = ({ showMessage }: { showMessage: (msg: string, type: 'success' | 'error') => void }) => {
  const [profile, setProfile] = useState<BusinessProfile>({
    commercialName: '', taxId: '', contactEmail: '', whatsAppNumber: '', description: ''
  });
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    loadProfile();
  }, []);

  const loadProfile = async () => {
    try {
      const data = await getBusinessProfile();
      if (data) setProfile(data);
    } catch (error: any) {
      showMessage(error.message || 'No se pudo cargar el perfil del negocio', 'error');
    } finally {
      setIsLoading(false);
    }
  };

  const handleSaveProfile = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    try {
      await updateBusinessProfile(profile);
      showMessage('Perfil guardado correctamente', 'success');
    } catch (error: any) { 
      showMessage(error.message || 'Error al guardar el perfil', 'error'); 
    } finally { 
      setIsSaving(false); 
    }
  };

  if (isLoading) return <div className="p-6 text-center text-gray-500">Cargando perfil...</div>;

  return (
    <form onSubmit={handleSaveProfile} className="bg-white shadow-sm border border-gray-200 rounded-xl p-6 animate-in fade-in">
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-6">
        <div>
          <label className="block text-sm font-medium mb-1">Nombre Comercial</label>
          <input type="text" value={profile.commercialName} onChange={e => setProfile({...profile, commercialName: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" required />
        </div>
        <div>
          <label className="block text-sm font-medium mb-1">Tax ID (RUC)</label>
          <input type="text" value={profile.taxId} onChange={e => setProfile({...profile, taxId: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" />
        </div>
        <div>
          <label className="block text-sm font-medium mb-1">Correo Electrónico</label>
          <input type="email" value={profile.contactEmail} onChange={e => setProfile({...profile, contactEmail: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" />
        </div>
        <div>
          <label className="block text-sm font-medium mb-1">WhatsApp</label>
          <input type="tel" value={profile.whatsAppNumber} onChange={e => setProfile({...profile, whatsAppNumber: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" required />
        </div>
      </div>
      <div className="mb-6">
        <label className="block text-sm font-medium mb-1">Descripción</label>
        <textarea rows={3} value={profile.description} onChange={e => setProfile({...profile, description: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" />
      </div>
      <div className="flex justify-end">
        <button type="submit" disabled={isSaving} className="px-5 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50">
          {isSaving ? 'Guardando...' : 'Guardar Perfil'}
        </button>
      </div>
    </form>
  );
};