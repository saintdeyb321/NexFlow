import { Button } from '../../components/ui/Button';
import type { ErrorInfo, ReactNode } from 'react';
import { AlertTriangle } from 'lucide-react';
import { Component } from 'react';

interface Props {
  children?: ReactNode;
}

interface State {
  hasError: boolean;
}

export class ErrorBoundary extends Component<Props, State> {
  public state: State = {
    hasError: false
  };

  public static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    if (import.meta.env.DEV) console.error('Error no capturado (UI):', error, errorInfo);
  }

  public render() {
    if (this.state.hasError) {
      return (
        <div className="flex flex-col items-center justify-center h-screen bg-gray-50 text-center p-6">
          <AlertTriangle className="w-16 h-16 text-red-500 mb-4" />
          <h1 className="text-2xl font-bold text-gray-900 mb-2">Ups, algo salió mal en la interfaz.</h1>
          <p className="text-gray-600 max-w-md mb-6">
            No pudimos mostrar esta pantalla. Puedes reintentar o volver al inicio.
          </p>
          <div className="flex gap-3">
            <Button variant="secondary" onClick={() => this.setState({ hasError: false })}>Reintentar</Button>
            <Button onClick={() => window.location.href = '/'}>Volver al Inicio</Button>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
