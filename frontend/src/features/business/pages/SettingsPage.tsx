import { useState } from 'react';
import { Building2, MapPin, Clock, MessageCircle } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { ProfileTab } from '../components/ProfileTab';
import { LocationsTab } from '../components/LocationsTab';
import { HoursTab } from '../components/HoursTab';
import { WhatsAppTab } from '../components/WhatsAppTab';

export const SettingsPage = () => {
  const { me } = useAuthStore();
  const workspaceId = me?.workspace?.id;

  const [activeTab, setActiveTab] = useState<'profile' | 'locations' | 'hours' | 'whatsapp'>('profile');
  const [message, setMessage] = useState({ text: '', type: '' });

  const showMessage = (text: string, type: 'success' | 'error') => {
    setMessage({ text, type });
    setTimeout(() => setMessage({ text: '', type: '' }), 4000);
  };

  if (!workspaceId) return <div className="text-center p-8 text-gray-500">Sin negocio asignado</div>;

  return (
    <div className="max-w-4xl mx-auto animate-in fade-in slide-in-from-bottom-2">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900 flex items-center">Configuración del Negocio</h1>
        <p className="mt-1 text-sm text-gray-500">Administra la identidad, ubicaciones, horarios de atención y tu conexión a WhatsApp.</p>
      </div>

      <div className="flex border-b border-gray-200 mb-6 overflow-x-auto">
        <button onClick={() => setActiveTab('profile')} className={`whitespace-nowrap px-4 py-2 border-b-2 font-medium text-sm transition-colors ${activeTab === 'profile' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
          <div className="flex items-center"><Building2 className="w-4 h-4 mr-2"/> Perfil</div>
        </button>
        <button onClick={() => setActiveTab('locations')} className={`whitespace-nowrap px-4 py-2 border-b-2 font-medium text-sm transition-colors ${activeTab === 'locations' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
          <div className="flex items-center"><MapPin className="w-4 h-4 mr-2"/> Sedes</div>
        </button>
        <button onClick={() => setActiveTab('hours')} className={`whitespace-nowrap px-4 py-2 border-b-2 font-medium text-sm transition-colors ${activeTab === 'hours' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
          <div className="flex items-center"><Clock className="w-4 h-4 mr-2"/> Horarios</div>
        </button>
        <button onClick={() => setActiveTab('whatsapp')} className={`whitespace-nowrap px-4 py-2 border-b-2 font-medium text-sm transition-colors ${activeTab === 'whatsapp' ? 'border-green-600 text-green-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
          <div className="flex items-center"><MessageCircle className="w-4 h-4 mr-2"/> WhatsApp</div>
        </button>
      </div>

      {message.text && (
        <div className={`mb-6 p-4 rounded-lg text-sm font-medium animate-in fade-in slide-in-from-top-2 ${message.type === 'success' ? 'bg-green-50 text-green-700 border border-green-200' : 'bg-red-50 text-red-700 border border-red-200'}`}>
          {message.text}
        </div>
      )}

      <div className="mt-2">
        {activeTab === 'profile' && <ProfileTab showMessage={showMessage} />}
        {activeTab === 'locations' && <LocationsTab showMessage={showMessage} />}
        {activeTab === 'hours' && <HoursTab showMessage={showMessage} />}
        {activeTab === 'whatsapp' && <WhatsAppTab showMessage={showMessage} />}
      </div>
    </div>
  );
};