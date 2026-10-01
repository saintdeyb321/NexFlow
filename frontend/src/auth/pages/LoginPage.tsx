import { Navigate } from 'react-router-dom';
import { MessageSquare, Sparkles, CalendarCheck, Layers, ArrowRight } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { Alert } from '../../components/ui/Feedback';
import { useGoogleLogin } from '../hooks/useGoogleLogin';
import { useAuthStore } from '../../core/store/useAuthStore';

export const LoginPage = () => {
  const { login, isLoading, error } = useGoogleLogin();
  const { isAuthenticated } = useAuthStore();
  if (isAuthenticated) return <Navigate to="/" replace />;
  return (
    <div className="min-h-dvh grid lg:grid-cols-2 bg-background">
      <section className="relative bg-sidebar text-white p-7 sm:p-12 lg:p-16 flex flex-col justify-between overflow-hidden">
        <div aria-hidden="true" className="absolute -right-32 top-24 w-96 h-96 rounded-full bg-primary/15 blur-3xl" />
        <div className="relative flex items-center gap-3"><span className="w-10 h-10 bg-primary rounded-xl flex items-center justify-center"><Layers aria-hidden="true" className="w-5 h-5" /></span><span className="text-2xl font-semibold tracking-tight">NexFlow<span className="text-accent">.</span></span></div>
        <div className="relative py-6 lg:py-20 max-w-lg">
          <p className="text-xs text-cyan-300 uppercase tracking-[.2em] font-semibold mb-5">Comunicación que fluye</p>
          <h1 className="text-3xl sm:text-4xl xl:text-5xl font-semibold leading-tight tracking-tight">Cada conversación,<br /><span className="text-indigo-300">en un solo lugar.</span></h1>
          <p className="mt-6 text-slate-300 text-base leading-relaxed">Centraliza la atención de tu negocio en WhatsApp y combina automatización con el acompañamiento de tu equipo.</p>
          <ul className="hidden lg:block mt-8 space-y-4 text-sm text-slate-300">
            <li className="flex gap-3 items-center"><MessageSquare aria-hidden="true" className="w-5 h-5 text-cyan-300 shrink-0" /> Atención conversacional con IA y humanos</li>
            <li className="flex gap-3 items-center"><CalendarCheck aria-hidden="true" className="w-5 h-5 text-cyan-300 shrink-0" /> Reservas y solicitudes según tus módulos</li>
            <li className="flex gap-3 items-center"><Sparkles aria-hidden="true" className="w-5 h-5 text-cyan-300 shrink-0" /> Gestión centralizada de tu negocio</li>
          </ul>
        </div>
        <p className="relative text-xs text-slate-400">NexFlow · Plataforma de comunicación empresarial</p>
      </section>
      <main className="flex items-center justify-center p-5 sm:p-12">
        <div className="nf-panel w-full max-w-md p-6 sm:p-10">
          <span className="inline-flex h-12 w-12 items-center justify-center bg-indigo-50 text-primary rounded-2xl mb-6"><ArrowRight aria-hidden="true" className="w-6 h-6" /></span>
          <h2 className="text-2xl font-semibold tracking-tight">Bienvenido a NexFlow</h2>
          <p className="text-muted text-sm mt-3 mb-8 leading-relaxed">Ingresa con tu cuenta de Google para acceder a tu workspace.</p>
          {error && <Alert tone="error" className="mb-6">{error}</Alert>}
          <Button variant="secondary" onClick={login} isLoading={isLoading} className="w-full">
            {!isLoading && (<svg aria-hidden="true" className="w-5 h-5" viewBox="0 0 48 48">
            <path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"/>
            <path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"/>
            <path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"/>
            <path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"/>
          </svg>)}
            {isLoading ? 'Conectando con Google...' : 'Continuar con Google'}
          </Button>
          <p className="mt-6 text-xs leading-relaxed text-muted text-center">Acceso con Google. Tus permisos y módulos corresponden al workspace de tu cuenta.</p>
        </div>
      </main>
    </div>
  );
};
