import { useState, useEffect } from 'react';
import { Building2, MapPin, Clock } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getBusinessProfile, updateBusinessProfile, getBusinessHours, saveBusinessHours, getLocations, saveLocation } from '../services/business.service';
import type { BusinessProfile, BusinessHoursDto, LocationDto } from '../types/business.types';

const DAYS_OF_WEEK = [
  { id: 1, name: 'Lunes' }, { id: 2, name: 'Martes' }, { id: 3, name: 'Miércoles' },
  { id: 4, name: 'Jueves' }, { id: 5, name: 'Viernes' }, { id: 6, name: 'Sábado' }, { id: 0, name: 'Domingo' }
];

export const SettingsPage = () => {
  const { me } = useAuthStore();
  const workspaceId = me?.workspace?.id;

  const [activeTab, setActiveTab] = useState<'profile' | 'locations' | 'hours'>('profile');
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [message, setMessage] = useState({ text: '', type: '' });
  
  const [profile, setProfile] = useState<BusinessProfile>({
    commercialName: '', taxId: '', contactEmail: '', whatsAppNumber: '', description: ''
  });
  const [hours, setHours] = useState<BusinessHoursDto[]>(
    DAYS_OF_WEEK.map(d => ({ dayOfWeek: d.id, openTime: '08:00', closeTime: '18:00', isClosed: d.id === 0 }))
  );
  
  // NUEVO: Estado para Sedes reales
  const [locations, setLocations] = useState<LocationDto[]>([]);
  const [newLocation, setNewLocation] = useState<Partial<LocationDto>>({ name: '', address: '', reference: '', isMain: true });

  useEffect(() => {
    if (workspaceId) loadData();
    else setIsLoading(false);
  }, [workspaceId]);

  const loadData = async () => {
    try {
      const [profileData, hoursData, locsData] = await Promise.all([
        getBusinessProfile().catch(() => null),
        getBusinessHours().catch(() => []),
        getLocations().catch(() => [])
      ]);
      if (profileData) setProfile(profileData);
      if (hoursData && hoursData.length > 0) setHours(hoursData);
      if (locsData) setLocations(locsData);
    } catch (error) {
      console.error("Error cargando configuración", error);
    } finally {
      setIsLoading(false);
    }
  };

  const handleSaveProfile = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    try {
      await updateBusinessProfile(profile);
      setMessage({ text: 'Perfil guardado', type: 'success' });
    } catch { setMessage({ text: 'Error guardando perfil', type: 'error' }); } 
    finally { setIsSaving(false); }
  };

  const handleSaveHours = async () => {
    setIsSaving(true);
    try {
      await saveBusinessHours(hours);
      setMessage({ text: 'Horarios actualizados', type: 'success' });
    } catch { setMessage({ text: 'Error guardando horarios', type: 'error' }); } 
    finally { setIsSaving(false); }
  };

  const handleSaveLocation = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    try {
      // Forzamos isMain a true si es la primera sede
      const locToSave = { ...newLocation, isMain: locations.length === 0 ? true : newLocation.isMain } as LocationDto;
      await saveLocation(locToSave);
      setMessage({ text: 'Sede registrada exitosamente', type: 'success' });
      
      // Recargar sedes y limpiar formulario
      const updatedLocs = await getLocations();
      setLocations(updatedLocs);
      setNewLocation({ name: '', address: '', reference: '', isMain: false });
    } catch { setMessage({ text: 'Error guardando la sede', type: 'error' }); } 
    finally { setIsSaving(false); }
  };

  const updateHour = (day: number, field: keyof BusinessHoursDto, value: any) => {
    setHours(hours.map(h => h.dayOfWeek === day ? { ...h, [field]: value } : h));
  };

  if (isLoading) return <div className="animate-pulse flex justify-center h-64 items-center">Cargando...</div>;
  if (!workspaceId) return <div className="text-center p-8">Sin negocio asignado</div>;

  return (
    <div className="max-w-4xl">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900 flex items-center">Configuración del Negocio</h1>
        <p className="mt-1 text-sm text-gray-500">Administra la identidad, ubicaciones y horarios de atención.</p>
      </div>

      <div className="flex border-b border-gray-200 mb-6">
        <button onClick={() => setActiveTab('profile')} className={`px-4 py-2 border-b-2 font-medium text-sm ${activeTab === 'profile' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
          <div className="flex items-center"><Building2 className="w-4 h-4 mr-2"/> Perfil</div>
        </button>
        <button onClick={() => setActiveTab('locations')} className={`px-4 py-2 border-b-2 font-medium text-sm ${activeTab === 'locations' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
          <div className="flex items-center"><MapPin className="w-4 h-4 mr-2"/> Sedes</div>
        </button>
        <button onClick={() => setActiveTab('hours')} className={`px-4 py-2 border-b-2 font-medium text-sm ${activeTab === 'hours' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
          <div className="flex items-center"><Clock className="w-4 h-4 mr-2"/> Horarios</div>
        </button>
      </div>

      {message.text && (
        <div className={`mb-4 p-3 rounded text-sm ${message.type === 'success' ? 'bg-green-50 text-green-700' : 'bg-red-50 text-red-700'}`}>
          {message.text}
        </div>
      )}

      {activeTab === 'profile' && (
        <form onSubmit={handleSaveProfile} className="bg-white shadow-sm border border-gray-200 rounded-xl p-6">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-6">
            <div><label className="block text-sm font-medium mb-1">Nombre Comercial</label><input type="text" value={profile.commercialName} onChange={e => setProfile({...profile, commercialName: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" required /></div>
            <div><label className="block text-sm font-medium mb-1">Tax ID (RUC)</label><input type="text" value={profile.taxId} onChange={e => setProfile({...profile, taxId: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" /></div>
            <div><label className="block text-sm font-medium mb-1">Correo</label><input type="email" value={profile.contactEmail} onChange={e => setProfile({...profile, contactEmail: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" /></div>
            <div><label className="block text-sm font-medium mb-1">WhatsApp</label><input type="tel" value={profile.whatsAppNumber} onChange={e => setProfile({...profile, whatsAppNumber: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" required /></div>
          </div>
          <div className="mb-6"><label className="block text-sm font-medium mb-1">Descripción</label><textarea rows={3} value={profile.description} onChange={e => setProfile({...profile, description: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" /></div>
          <div className="flex justify-end"><button type="submit" disabled={isSaving} className="px-5 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700">{isSaving ? 'Guardando...' : 'Guardar Perfil'}</button></div>
        </form>
      )}

      {activeTab === 'hours' && (
        <div className="bg-white shadow-sm border border-gray-200 rounded-xl p-6">
          <div className="space-y-4">
            {DAYS_OF_WEEK.map(day => {
              const h = hours.find(x => x.dayOfWeek === day.id) || { openTime: '', closeTime: '', isClosed: true };
              return (
                <div key={day.id} className="flex items-center justify-between border-b pb-3">
                  <div className="w-32 font-medium text-gray-700">{day.name}</div>
                  <div className="flex items-center space-x-4">
                    <label className="flex items-center text-sm text-gray-600">
                      <input type="checkbox" checked={h.isClosed} onChange={(e) => updateHour(day.id, 'isClosed', e.target.checked)} className="mr-2 rounded text-blue-600" />
                      Cerrado
                    </label>
                    <input type="time" disabled={h.isClosed} value={h.openTime} onChange={(e) => updateHour(day.id, 'openTime', e.target.value)} className="border rounded px-2 py-1 text-sm disabled:opacity-50" />
                    <span className="text-gray-400">-</span>
                    <input type="time" disabled={h.isClosed} value={h.closeTime} onChange={(e) => updateHour(day.id, 'closeTime', e.target.value)} className="border rounded px-2 py-1 text-sm disabled:opacity-50" />
                  </div>
                </div>
              )
            })}
          </div>
          <div className="flex justify-end mt-6">
            <button onClick={handleSaveHours} disabled={isSaving} className="px-5 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700">{isSaving ? 'Guardando...' : 'Guardar Horarios'}</button>
          </div>
        </div>
      )}

      {activeTab === 'locations' && (
        <div className="space-y-6">
          {/* Formulario de Nueva Sede */}
          <form onSubmit={handleSaveLocation} className="bg-white shadow-sm border border-gray-200 rounded-xl p-6">
            <h3 className="text-lg font-bold text-gray-900 mb-4">Registrar Nueva Sede</h3>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-4">
              <div><label className="block text-sm font-medium mb-1">Nombre (Ej: Sucursal Centro)</label><input type="text" value={newLocation.name} onChange={e => setNewLocation({...newLocation, name: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" required /></div>
              <div><label className="block text-sm font-medium mb-1">Dirección Exacta</label><input type="text" value={newLocation.address} onChange={e => setNewLocation({...newLocation, address: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" required /></div>
            </div>
            <div className="mb-4">
              <label className="block text-sm font-medium mb-1">Referencia</label>
              <input type="text" value={newLocation.reference} onChange={e => setNewLocation({...newLocation, reference: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" placeholder="Ej: Frente al parque central" />
            </div>
            <div className="flex justify-end">
              <button type="submit" disabled={isSaving} className="px-5 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700">{isSaving ? 'Guardando...' : 'Añadir Sede'}</button>
            </div>
          </form>

          {/* Lista de Sedes Reales */}
          <div className="bg-white shadow-sm border border-gray-200 rounded-xl p-6">
            <h3 className="text-lg font-bold text-gray-900 mb-4">Tus Sedes Registradas</h3>
            {locations.length === 0 ? (
              <p className="text-sm text-gray-500 italic">Aún no hay sedes registradas.</p>
            ) : (
              <ul className="divide-y divide-gray-100">
                {locations.map(loc => (
                  <li key={loc.id} className="py-3 flex justify-between items-start">
                    <div>
                      <h4 className="font-medium text-gray-900 flex items-center">
                        {loc.name} {loc.isMain && <span className="ml-2 px-2 py-0.5 bg-green-100 text-green-700 text-xs rounded-full">Sede Principal</span>}
                      </h4>
                      <p className="text-sm text-gray-500 mt-1">{loc.address}</p>
                      {loc.reference && <p className="text-xs text-gray-400 mt-0.5">Ref: {loc.reference}</p>}
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </div>
  );
};