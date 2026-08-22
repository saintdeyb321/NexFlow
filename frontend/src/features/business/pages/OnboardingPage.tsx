import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Building2, Clock, CheckCircle } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { updateBusinessProfile, saveBusinessHours, completeBusinessOnboarding } from '../services/business.service';
import type { BusinessProfile, BusinessHoursDto } from '../types/business.types';

const DAYS_OF_WEEK = [
  { id: 1, name: 'Lunes' }, { id: 2, name: 'Martes' }, { id: 3, name: 'Miércoles' },
  { id: 4, name: 'Jueves' }, { id: 5, name: 'Viernes' }, { id: 6, name: 'Sábado' }, { id: 0, name: 'Domingo' }
];

export const OnboardingPage = () => {
  const navigate = useNavigate();
  const { completeOnboarding } = useAuthStore();
  const [step, setStep] = useState(1);
  const [isSaving, setIsSaving] = useState(false);

  const [profile, setProfile] = useState<BusinessProfile>({
    commercialName: '', taxId: '', contactEmail: '', whatsAppNumber: '', description: ''
  });
  
  const [hours, setHours] = useState<BusinessHoursDto[]>(
    DAYS_OF_WEEK.map(d => ({ dayOfWeek: d.id, openTime: '08:00', closeTime: '18:00', isClosed: d.id === 0 }))
  );

  const handleNext = async () => {
    if (step === 1) {
      if (!profile.commercialName || !profile.whatsAppNumber) return alert('Nombre y WhatsApp son obligatorios.');
      setIsSaving(true);
      try {
        await updateBusinessProfile(profile);
        setStep(2);
      } catch (e) { alert('Error guardando perfil'); }
      finally { setIsSaving(false); }
    } else if (step === 2) {
      setIsSaving(true);
      try {
        await saveBusinessHours(hours);
        setStep(3);
      } catch (e) { alert('Error guardando horarios'); }
      finally { setIsSaving(false); }
    }
  };

  const handleFinish = async () => {
    setIsSaving(true);
    try {
      // 1. Marcamos el workspace como 'Active' en la Base de Datos (PostgreSQL)
      await completeBusinessOnboarding();
      
      // 2. Actualizamos el estado global en memoria (Zustand) para no requerir un refresh
      await completeOnboarding(); 
      
      // 3. ¡Al Dashboard!
      navigate('/');
    } catch (e) {
      alert('Hubo un error finalizando el onboarding. Por favor intenta de nuevo.');
    } finally {
      setIsSaving(false);
    }
  };

  const updateHour = (day: number, field: keyof BusinessHoursDto, value: any) => {
    setHours(hours.map(h => h.dayOfWeek === day ? { ...h, [field]: value } : h));
  };

  return (
    <div className="min-h-screen bg-gray-50 flex flex-col items-center justify-center py-10 px-4">
      <div className="max-w-3xl w-full bg-white rounded-2xl shadow-xl overflow-hidden">
        
        {/* Progress Header */}
        <div className="bg-purple-600 px-8 py-6 text-white flex justify-between items-center">
          <div>
            <h1 className="text-2xl font-bold">¡Bienvenido a NexFlow!</h1>
            <p className="opacity-80 text-sm mt-1">Configuremos tu espacio de trabajo (Paso {step} de 3)</p>
          </div>
          <div className="flex space-x-2">
            {[1, 2, 3].map(i => (
              <div key={i} className={`w-3 h-3 rounded-full ${step >= i ? 'bg-white' : 'bg-purple-400 opacity-50'}`} />
            ))}
          </div>
        </div>

        {/* Formularios */}
        <div className="p-8">
          {step === 1 && (
            <div className="animate-in fade-in">
              <div className="flex items-center mb-6">
                <div className="w-10 h-10 rounded-full bg-purple-100 text-purple-600 flex items-center justify-center mr-4"><Building2 className="w-5 h-5"/></div>
                <h2 className="text-xl font-semibold text-gray-800">Perfil del Negocio</h2>
              </div>
              <div className="space-y-4">
                <div><label className="block text-sm font-medium mb-1">Nombre Comercial <span className="text-red-500">*</span></label><input type="text" value={profile.commercialName} onChange={e => setProfile({...profile, commercialName: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" placeholder="Ej: Clínica Dental San José" /></div>
                <div><label className="block text-sm font-medium mb-1">Número de WhatsApp Principal <span className="text-red-500">*</span></label><input type="tel" value={profile.whatsAppNumber} onChange={e => setProfile({...profile, whatsAppNumber: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" placeholder="Ej: +51 987654321" /></div>
                <div><label className="block text-sm font-medium mb-1">Breve Descripción</label><textarea value={profile.description} onChange={e => setProfile({...profile, description: e.target.value})} rows={2} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" placeholder="¿A qué se dedica tu negocio?" /></div>
              </div>
            </div>
          )}

          {step === 2 && (
            <div className="animate-in fade-in">
              <div className="flex items-center mb-6">
                <div className="w-10 h-10 rounded-full bg-purple-100 text-purple-600 flex items-center justify-center mr-4"><Clock className="w-5 h-5"/></div>
                <h2 className="text-xl font-semibold text-gray-800">Horarios de Atención</h2>
              </div>
              <p className="text-sm text-gray-500 mb-6">La IA utilizará estos horarios para ofrecer citas a tus clientes.</p>
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

          {step === 3 && (
            <div className="text-center animate-in fade-in py-8">
              <CheckCircle className="w-20 h-20 text-green-500 mx-auto mb-4" />
              <h2 className="text-2xl font-bold text-gray-800 mb-2">¡Todo listo!</h2>
              <p className="text-gray-600">Tu asistente de inteligencia artificial ya tiene la información base para operar.</p>
            </div>
          )}
        </div>

        {/* Footer Navigation */}
        <div className="px-8 py-5 bg-gray-50 border-t flex justify-end">
          {step < 3 ? (
            <button onClick={handleNext} disabled={isSaving} className="px-6 py-2 bg-purple-600 text-white font-medium rounded-lg hover:bg-purple-700 disabled:opacity-50">
              {isSaving ? 'Guardando...' : 'Siguiente'}
            </button>
          ) : (
            <button onClick={handleFinish} className="px-6 py-2 bg-green-600 text-white font-medium rounded-lg hover:bg-green-700">
              Ir al Dashboard
            </button>
          )}
        </div>
      </div>
    </div>
  );
};