import { useState } from 'react';
import type { ReactNode } from 'react';
import { Image } from 'lucide-react';

export const OfferingImage = ({ src, name, icon = <Image className="w-8 h-8" />, className = '' }: { src?: string | null; name: string; icon?: ReactNode; className?: string }) => {
  const [failedSrc, setFailedSrc] = useState<string | null>(null);
  return <div className={`bg-surface-soft overflow-hidden flex items-center justify-center text-slate-300 ${className}`}>
    {src && failedSrc !== src ? <img src={src} alt={name} loading="lazy" onError={() => setFailedSrc(src)} className="w-full h-full object-cover" />
      : <span aria-hidden="true">{icon}</span>}
  </div>;
};
