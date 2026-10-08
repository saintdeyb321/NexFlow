import { ChevronLeft, ChevronRight } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { FormField, Input } from '../../../components/ui/Form';
import { addCivilDays, formatWeek, weekOf } from '../utils/weeklyAgenda';

interface WeekNavigationProps {
  from: string;
  to: string;
  today: string;
  onChange: (date: string) => void;
  onCurrentWeek?: () => void;
}
export const WeekNavigation = ({ from, to, today, onChange, onCurrentWeek }: WeekNavigationProps) => {
  const canMove = (days: number) => { try { weekOf(addCivilDays(from, days)); return true; } catch { return false; } };
  return <section aria-label="Navegación semanal" className="nf-panel p-4 sm:p-5 mb-5 flex flex-wrap items-end justify-between gap-4">
    <div className="space-y-3 min-w-0">
      <h2 aria-live="polite" className="text-lg font-semibold text-foreground">{formatWeek(from, to)}</h2>
      <div className="flex flex-wrap gap-2">
        <Button variant="secondary" aria-label="Semana anterior" disabled={!canMove(-7)} onClick={() => onChange(addCivilDays(from, -7))}><ChevronLeft aria-hidden="true" className="w-4 h-4" />Anterior</Button>
        <Button variant="secondary" onClick={onCurrentWeek ?? (() => onChange(today))}>Esta semana</Button>
        <Button variant="secondary" aria-label="Semana siguiente" disabled={!canMove(7)} onClick={() => onChange(addCivilDays(from, 7))}>Siguiente<ChevronRight aria-hidden="true" className="w-4 h-4" /></Button>
      </div>
    </div>
    <FormField label="Ir a fecha" className="w-full sm:w-44"><Input type="date" value={from} onChange={event => { if (event.target.value) onChange(event.target.value); }} /></FormField>
  </section>;
};
