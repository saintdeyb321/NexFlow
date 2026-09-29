export const getBusinessToday = (timeZone: string): string => {
  const today = new Date();
  const formatter = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit'
  });
  return formatter.format(today); // Retorna 'YYYY-MM-DD'
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
      hour12: false
    });
    const parts = formatter.formatToParts(date);
    const getPart = (type: string) => parts.find(p => p.type === type)?.value;
    
    return `${getPart('year')}-${getPart('month')}-${getPart('day')}T${getPart('hour')}:${getPart('minute')}`;
  } catch {
    return '';
  }
};