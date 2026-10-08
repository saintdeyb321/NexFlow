export const resolveBusinessTimeZone = (timeZone?: string | null): string => {
  try {
    return new Intl.DateTimeFormat('es-PE', { timeZone: timeZone || 'America/Lima' }).resolvedOptions().timeZone;
  } catch {
    return 'America/Lima';
  }
};

export const getBusinessToday = (timeZone: string, today = new Date()): string => {
  const formatter = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit'
  });
  const parts = formatter.formatToParts(today);
  const part = (type: string) => parts.find(value => value.type === type)?.value;
  return `${part('year')}-${part('month')}-${part('day')}`;
};

export const formatBusinessDateTime = (isoDate: string, timeZone: string): string => {
  try {
    const date = new Date(isoDate);
    return new Intl.DateTimeFormat('es-PE', {
      timeZone,
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    }).format(date);
  } catch {
    return 'Fecha inválida';
  }
};

export const toBusinessLocalInput = (isoDate: string, timeZone: string): string => {
  try {
    const date = new Date(isoDate);
    const formatter = new Intl.DateTimeFormat('en-CA', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23'
    });
    const parts = formatter.formatToParts(date);
    const getPart = (type: string) => parts.find(p => p.type === type)?.value;
    
    return `${getPart('year')}-${getPart('month')}-${getPart('day')}T${getPart('hour')}:${getPart('minute')}`;
  } catch {
    return '';
  }
};
