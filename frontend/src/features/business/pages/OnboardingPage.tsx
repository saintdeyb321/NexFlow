import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { Building2, Clock, CheckCircle, MapPin } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getBusinessProfile, updateBusinessProfile, saveLocation, getLocations, saveBusinessHours, completeBusinessOnboarding } from '../services/business.service';
import type { BusinessProfile, BusinessHoursDto, LocationDto } from '../types/business.types';

const DAYS_OF_WEEK = [
  { id: 1, name: 'Lunes' }, { id: 2, name: 'Martes' }, { id: 3, name: 'Miércoles' },
  { id: 4, name: 'Jueves' }, { id: 5, name: 'Viernes' }, { id: 6, name: 'Sábado' }, { id: 0, name: 'Domingo' }
];

export const OnboardingPage = () => {
  const navigate = useNavigate();
  const { completeOnboarding } = useAuthStore();
  const [step, setStep] = useState(1);
  const [isSaving, setIsSaving] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [mainLocationId, setMainLocationId] = useState<string>('');

  const [profile, setProfile] = useState<BusinessProfile>({ commercialName: '', taxId: '', contactEmail: '', whatsAppNumber: '', description: '' });
  const [location, setLocation] = useState<LocationDto>({ name: 'Sede Principal', address: '', reference: '', isMain: true });
  const [hours, setHours] = useState<BusinessHoursDto[]>(DAYS_OF_WEEK.map(d => ({ dayOfWeek: d.id, openTime: '08:00', closeTime: '18:00', isClosed: d.id === 0 })));

  // 🔥 CORRECCIÓN: Cargamos el perfil inicial para no empezar en blanco si ya había un borrador
  useEffect(() => {
    const fetchProfile = async () => {
      try {
        const data = await getBusinessProfile();
        if (data && data.commercialName) {
          setProfile(data);
        }
      } catch (error) {
        console.warn("No se pudo cargar el perfil base.");
      } finally {
        setIsLoading(false);
      }
    };
    fetchProfile();
  }, []);

  const handleNext = async () => {
    setIsSaving(true);
    try {
      if (step === 1) {
        if (!profile.commercialName || !profile.whatsAppNumber) return alert('Nombre y WhatsApp son obligatorios.');
        await updateBusinessProfile(profile);
        setStep(2);
      } 
      else if (step === 2) {
        if (!location.name || !location.address) return alert('El nombre y la dirección de la sede son obligatorios.');
        
        await saveLocation(location);
        
        // Recuperamos el ID que Firestore le asignó a la sede
        const locs = await getLocations();
        const mainLoc = locs.find(l => l.isMain) || locs[0];
        setMainLocationId(mainLoc.id!);
        setStep(3);
      } 
      else if (step === 3) {
        await saveBusinessHours(mainLocationId, hours);
        setStep(4);
      }
    } catch (e: any) {
      // 🔥 CORRECCIÓN: Mostrar error real si excede límite de sedes o hay fallo de red
      alert(`Error al guardar: ${e.response?.data?.error || e.message || 'Error desconocido'}`);
      console.error(e);
    } finally {
      setIsSaving(false);
    }
  };

  const handleFinish = async () => {
    setIsSaving(true);
    try {
      await completeBusinessOnboarding();
      await completeOnboarding(); 
      navigate('/');
    } catch (e) { alert('Error finalizando el onboarding.'); } 
    finally { setIsSaving(false); }
  };

  const updateHour = (day: number, field: keyof BusinessHoursDto, value: any) => {
    setHours(hours.map(h => h.dayOfWeek === day ? { ...h, [field]: value } : h));
  };

  if (isLoading) return <div className="h-screen flex items-center justify-center text-gray-500">Cargando Onboarding...</div>;

  return (
    <div className="min-h-screen bg-gray-50 flex flex-col items-center justify-center py-10 px-4">
      <div className="max-w-3xl w-full bg-white rounded-2xl shadow-xl overflow-hidden">
        
        <div className="bg-purple-600 px-8 py-6 text-white flex justify-between items-center">
          <div>
            <h1 className="text-2xl font-bold">¡Bienvenido a NexFlow!</h1>
            <p className="opacity-80 text-sm mt-1">Configuremos tu espacio de trabajo (Paso {step} de 4)</p>
          </div>
          <div className="flex space-x-2">
            {[1, 2, 3, 4].map(i => (
              <div key={i} className={`w-3 h-3 rounded-full ${step >= i ? 'bg-white' : 'bg-purple-400 opacity-50'}`} />
            ))}
          </div>
        </div>

        <div className="p-8 min-h-[400px]">
          {step === 1 && (
            <div className="animate-in fade-in">
              <div className="flex items-center mb-6">
                <div className="w-10 h-10 rounded-full bg-purple-100 text-purple-600 flex items-center justify-center mr-4"><Building2 className="w-5 h-5"/></div>
                <h2 className="text-xl font-semibold text-gray-800">Perfil del Negocio</h2>
              </div>
              <div className="space-y-4">
                <div><label className="block text-sm font-medium mb-1">Nombre Comercial *</label><input type="text" value={profile.commercialName} onChange={e => setProfile({...profile, commercialName: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required /></div>
                <div><label className="block text-sm font-medium mb-1">Número de WhatsApp Principal *</label><input type="tel" value={profile.whatsAppNumber} onChange={e => setProfile({...profile, whatsAppNumber: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required /></div>
                <div><label className="block text-sm font-medium mb-1">Breve Descripción</label><textarea value={profile.description} onChange={e => setProfile({...profile, description: e.target.value})} rows={2} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" /></div>
              </div>
            </div>
          )}

          {step === 2 && (
            <div className="animate-in fade-in">
              <div className="flex items-center mb-6">
                <div className="w-10 h-10 rounded-full bg-purple-100 text-purple-600 flex items-center justify-center mr-4"><MapPin className="w-5 h-5"/></div>
                <h2 className="text-xl font-semibold text-gray-800">Sede Principal</h2>
              </div>
              <div className="space-y-4">
                <div><label className="block text-sm font-medium mb-1">Nombre de la Sede *</label><input type="text" value={location.name} onChange={e => setLocation({...location, name: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required /></div>
                <div><label className="block text-sm font-medium mb-1">Dirección Exacta *</label><input type="text" value={location.address} onChange={e => setLocation({...location, address: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required /></div>
                <div><label className="block text-sm font-medium mb-1">Referencia</label><input type="text" value={location.reference} onChange={e => setLocation({...location, reference: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" /></div>
                <div>
                  <label className="block text-sm font-medium mb-1">Enlace de Google Maps (Opcional)</label>
                  <input 
                    type="url" 
                    value={location.mapUrl || ''} 
                    onChange={e => setLocation({...location, mapUrl: e.target.value})} 
                    placeholder="https://maps.app.goo.gl/..."
                    className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" 
                  />
                </div>
              </div>
            </div>
          )}

          {step === 3 && (
            <div className="animate-in fade-in">
              <div className="flex items-center mb-6">
                <div className="w-10 h-10 rounded-full bg-purple-100 text-purple-600 flex items-center justify-center mr-4"><Clock className="w-5 h-5"/></div>
                <h2 className="text-xl font-semibold text-gray-800">Horarios de Atención</h2>
              </div>
              <div className="space-y-3">
                {DAYS_OF_WEEK.map(day => {
                  const h = hours.find(x => x.dayOfWeek === day.id)!;
                  return (
                    <div key={day.id} className="flex items-center justify-between border-b pb-2">
                      <div className="w-24 font-medium text-gray-700">{day.name}</div>
                      <div className="flex items-center space-x-3">
                        <label className="flex items-center text-sm text-gray-600"><input type="checkbox" checked={h.isClosed} onChange={(e) => updateHour(day.id, 'isClosed', e.target.checked)} className="mr-2 rounded text-purple-600" />Cerrado</label>
                        <input type="time" disabled={h.isClosed} value={h.openTime} onChange={(e) => updateHour(day.id, 'openTime', e.target.value)} className="border rounded px-2 py-1 text-sm disabled:opacity-50" />
                        <span className="text-gray-400">-</span>
                        <input type="time" disabled={h.isClosed} value={h.closeTime} onChange={(e) => updateHour(day.id, 'closeTime', e.target.value)} className="border rounded px-2 py-1 text-sm disabled:opacity-50" />
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>
          )}

          {step === 4 && (
            <div className="text-center animate-in fade-in py-8">
              <CheckCircle className="w-20 h-20 text-green-500 mx-auto mb-4" />
              <h2 className="text-2xl font-bold text-gray-800 mb-2">¡Todo listo!</h2>
              <p className="text-gray-600">Tu asistente de IA ya tiene la información base para operar.</p>
            </div>
          )}
        </div>

        <div className="px-8 py-5 bg-gray-50 border-t flex justify-end">
          {step < 4 ? (
            <button onClick={handleNext} disabled={isSaving} className="px-6 py-2 bg-purple-600 text-white font-medium rounded-lg hover:bg-purple-700 disabled:opacity-50 transition-colors">
              {isSaving ? 'Guardando...' : 'Siguiente'}
            </button>
          ) : (
            <button onClick={handleFinish} disabled={isSaving} className="px-6 py-2 bg-green-600 text-white font-medium rounded-lg hover:bg-green-700 transition-colors">
              Ir al Dashboard
            </button>
          )}
        </div>
      </div>
    </div>
  );
};