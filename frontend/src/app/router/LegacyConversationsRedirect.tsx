import { Navigate, useLocation } from 'react-router-dom';

export const LegacyConversationsRedirect = () => {
  const { search, hash } = useLocation();
  return <Navigate to={{ pathname: '/inbox', search, hash }} replace />;
};
