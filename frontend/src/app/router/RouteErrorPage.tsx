import { isRouteErrorResponse, Link, useRouteError } from 'react-router-dom';
import { ErrorState } from '../../components/ui/Feedback';
import { Button } from '../../components/ui/Button';

export const RouteErrorPage = () => {
  const error = useRouteError();
  const notFound = isRouteErrorResponse(error) && error.status === 404;
  return <main className="flex min-h-dvh items-center justify-center bg-background p-5">
    <ErrorState title={notFound ? 'No encontramos esta página' : 'No pudimos abrir esta página'}
      description={notFound ? 'El enlace puede haber cambiado. Vuelve al inicio para continuar.' : 'Ocurrió un problema al cargar la pantalla. Puedes volver al inicio o intentarlo nuevamente.'}
      action={<div className="flex flex-wrap justify-center gap-3">
        <Link to="/" replace className="nf-button nf-button-primary">Volver al inicio</Link>
        {!notFound && <Button variant="secondary" onClick={() => window.location.reload()}>Intentar nuevamente</Button>}
      </div>} />
  </main>;
};
