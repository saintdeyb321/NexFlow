import { useId, useState } from 'react';
import { Button } from '../../../components/ui/Button';
import { Alert, Badge } from '../../../components/ui/Feedback';
import { FormField, Input } from '../../../components/ui/Form';
import type { BusinessHoursDto } from '../types/business.types';
import { applyHours, DEFAULT_CLOSE, DEFAULT_OPEN, setDayOpen, validateHours, validateTimes, WEEK_DAYS } from '../utils/businessHours';
import type { HoursErrors, HoursSelection } from '../utils/businessHours';

interface HoursWeekEditorProps {
  hours: BusinessHoursDto[]; unconfigured: boolean; partial: boolean; edited: boolean; editable: boolean;
  saving: boolean; saveBlocked: boolean; error?: string; success?: string;
  onChange: (hours: BusinessHoursDto[]) => void; onDiscard: () => void; onSave: (hours: BusinessHoursDto[]) => void;
}
export const HoursWeekEditor = ({ hours, unconfigured, partial, edited, editable, saving, saveBlocked, error, success, onChange, onDiscard, onSave }: HoursWeekEditorProps) => {
  const id = useId();
  const [errors, setErrors] = useState<HoursErrors>({});
  const openDays = hours.filter(hour => !hour.isClosed).length;
  const change = (next: BusinessHoursDto[]) => { setErrors({}); onChange(next); };
  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (!editable || saveBlocked || (!edited && !unconfigured)) return;
    const nextErrors = validateHours(hours); setErrors(nextErrors);
    const invalidDay = WEEK_DAYS.find(day => nextErrors[day.id]);
    if (invalidDay) {
      const field = nextErrors[invalidDay.id]?.openTime ? 'open' : 'close';
      document.getElementById(`${id}-${invalidDay.id}-${field}`)?.focus(); return;
    }
    onSave(hours);
  };
  return <section className="nf-panel p-4 sm:p-6 space-y-5" aria-labelledby={`${id}-title`} aria-busy={saving}>
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h2 id={`${id}-title`} className="text-lg font-semibold">Horario semanal</h2><p className="text-sm text-muted mt-1">Configura la atención de esta sede y revisa la semana antes de guardar.</p></div>
      <Badge tone={edited || unconfigured ? 'warning' : 'neutral'}>{edited || unconfigured ? 'Sin guardar' : 'Horario guardado'}</Badge>
    </div>
    {!editable && <Alert>Solo lectura. Puedes consultar los horarios; necesitas permiso de edición para cambiarlos.</Alert>}
    {unconfigured && <Alert>Esta sede no tiene horarios configurados. Te proponemos lunes a sábado de 08:00 a 20:00 y domingo cerrado. Es una sugerencia editable que se guardará solo al pulsar Guardar horarios.
      {editable && hours.find(hour => hour.dayOfWeek === 0)?.isClosed && <Button variant="secondary" className="mt-3" disabled={saving} onClick={() => change(setDayOpen(hours, 0, true))}>Abrir también el domingo</Button>}
    </Alert>}
    {partial && <Alert>Hay días sin registrar. Se muestran cerrados en el borrador; los horarios existentes se conservan. Al guardar completarás los siete días.</Alert>}
    {error && <Alert tone="error">{error} Conservamos tus cambios. Revisa el horario y vuelve a guardar.</Alert>}
    {success && <Alert tone="success">{success}</Alert>}
    {editable && <BulkHours disabled={saving} onApply={change} hours={hours} />}
    <form noValidate onSubmit={submit} className="space-y-5">
      <fieldset disabled={!editable || saving} className="space-y-3 min-w-0">
        <legend className="font-semibold mb-3">Días de atención</legend>
        {WEEK_DAYS.map(day => {
          const hour = hours.find(row => row.dayOfWeek === day.id)!;
          return <div key={day.id} className="rounded-xl border border-line p-3 sm:p-4 grid gap-3 sm:grid-cols-[minmax(6rem,0.8fr)_minmax(7rem,0.8fr)_minmax(0,1fr)_minmax(0,1fr)] sm:items-center" data-day={day.id}>
            <h3 className="font-medium">{day.name}</h3>
            <label className="flex items-center gap-3 min-h-11 text-sm">
              <Input type="checkbox" aria-label={`Abierto ${day.name}`} checked={!hour.isClosed} onChange={event => change(setDayOpen(hours, day.id, event.target.checked))} />
              {hour.isClosed ? 'Cerrado' : 'Abierto'}
            </label>
            <div className="grid grid-cols-2 gap-3 sm:contents">
              <FormField id={`${id}-${day.id}-open`} label={`Apertura ${day.name}`} error={errors[day.id]?.openTime}>
                <Input type="time" step="60" disabled={hour.isClosed} value={hour.openTime} onChange={event => change(hours.map(row => row.dayOfWeek === day.id ? { ...row, openTime: event.target.value } : row))} />
              </FormField>
              <FormField id={`${id}-${day.id}-close`} label={`Cierre ${day.name}`} error={errors[day.id]?.closeTime}>
                <Input type="time" step="60" disabled={hour.isClosed} value={hour.closeTime} onChange={event => change(hours.map(row => row.dayOfWeek === day.id ? { ...row, closeTime: event.target.value } : row))} />
              </FormField>
            </div>
          </div>;
        })}
      </fieldset>
      <div className="rounded-xl bg-surface-soft border border-line p-4" aria-label="Resumen semanal">
        <h3 className="font-semibold">Resumen · {openDays} {openDays === 1 ? 'día abierto' : 'días abiertos'}</h3>
        <ul className="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-3 text-sm">
          {WEEK_DAYS.map(day => { const hour = hours.find(row => row.dayOfWeek === day.id)!; return <li key={day.id}><span className="font-medium">{day.name}:</span> {hour.isClosed ? 'Cerrado' : `${hour.openTime || 'Sin apertura'} – ${hour.closeTime || 'Sin cierre'}`}</li>; })}
        </ul>
      </div>
      {editable && <div className="border-t border-line pt-4 flex flex-wrap gap-3 items-center justify-between">
        <p role="status" className="text-sm text-muted">{saving ? 'Guardando horarios...' : edited || unconfigured ? 'Tienes cambios sin guardar.' : 'No hay cambios pendientes.'}</p>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" disabled={saving || !edited} onClick={() => { setErrors({}); onDiscard(); }}>Descartar cambios</Button>
          <Button type="submit" isLoading={saving} disabled={saveBlocked || (!edited && !unconfigured)}>Guardar horarios</Button>
        </div>
      </div>}
    </form>
  </section>;
};

const BulkHours = ({ hours, disabled, onApply }: { hours: BusinessHoursDto[]; disabled: boolean; onApply: (hours: BusinessHoursDto[]) => void }) => {
  const id = useId();
  const [selection, setSelection] = useState<HoursSelection>('weekdays');
  const [days, setDays] = useState<number[]>([]);
  const [openTime, setOpenTime] = useState(DEFAULT_OPEN);
  const [closeTime, setCloseTime] = useState(DEFAULT_CLOSE);
  const [errors, setErrors] = useState<{ openTime?: string; closeTime?: string; days?: string }>({});
  const [applied, setApplied] = useState(false);
  return <form noValidate className="rounded-xl bg-surface-soft border border-line p-4 space-y-4" onSubmit={event => {
    event.preventDefault(); if (disabled) return;
    const nextErrors = { ...validateTimes(openTime, closeTime), ...(selection === 'selected' && days.length === 0 ? { days: 'Selecciona al menos un día.' } : {}) };
    setErrors(nextErrors); setApplied(false);
    if (Object.keys(nextErrors).length) { document.getElementById(nextErrors.openTime ? `${id}-open` : nextErrors.closeTime ? `${id}-close` : `${id}-day-1`)?.focus(); return; }
    onApply(applyHours(hours, selection, days, openTime, closeTime)); setApplied(true);
  }}>
    <fieldset disabled={disabled} className="space-y-4 min-w-0">
      <legend className="font-semibold mb-1">Aplicar un horario a varios días</legend>
      <p className="text-sm text-muted">Elige los días y las horas una vez. Aplicar cambia el borrador; todavía debes guardar.</p>
      <div className="flex flex-wrap gap-x-5 gap-y-1">
        {([['all', 'Toda la semana'], ['weekdays', 'Lunes a sábado'], ['selected', 'Solo días seleccionados']] as const).map(([value, label]) => <label key={value} className="flex items-center gap-2 min-h-11 text-sm">
          <Input type="radio" name={`${id}-selection`} value={value} checked={selection === value} onChange={() => { setSelection(value); setApplied(false); }} />{label}
        </label>)}
      </div>
      {selection === 'selected' && <fieldset aria-describedby={errors.days ? `${id}-days-error` : undefined} className="flex flex-wrap gap-x-4 gap-y-1">
        <legend className="text-sm font-medium mb-1">Días seleccionados</legend>
        {WEEK_DAYS.map(day => <label key={day.id} className="flex items-center gap-2 min-h-11 text-sm"><Input id={`${id}-day-${day.id}`} type="checkbox" checked={days.includes(day.id)} onChange={event => { setDays(previous => event.target.checked ? [...previous, day.id] : previous.filter(value => value !== day.id)); setApplied(false); }} />{day.name}</label>)}
        {errors.days && <p id={`${id}-days-error`} role="alert" className="w-full text-sm text-danger">{errors.days}</p>}
      </fieldset>}
      <div className="grid grid-cols-2 gap-3 sm:max-w-md">
        <FormField id={`${id}-open`} label="Apertura para aplicar" error={errors.openTime}><Input type="time" step="60" value={openTime} onChange={event => { setOpenTime(event.target.value); setApplied(false); }} /></FormField>
        <FormField id={`${id}-close`} label="Cierre para aplicar" error={errors.closeTime}><Input type="time" step="60" value={closeTime} onChange={event => { setCloseTime(event.target.value); setApplied(false); }} /></FormField>
      </div>
      <Button type="submit" variant="secondary">Aplicar al borrador</Button>
    </fieldset>
    {applied && <p role="status" className="text-sm text-muted">Horario aplicado al borrador. Revisa el resumen antes de guardar.</p>}
  </form>;
};
