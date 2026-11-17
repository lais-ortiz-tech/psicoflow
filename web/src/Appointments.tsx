import { FormEvent, useCallback, useEffect, useRef, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCalendarDays, faChevronLeft, faChevronRight, faCircleInfo, faPlus } from '@fortawesome/free-solid-svg-icons'
import { api } from './api'

type Patient = {
  id: string
  name: string
  isArchived: boolean
}

function startOfWeek(value: Date) {
  const date = new Date(value)
  const day = date.getDay()
  date.setHours(0, 0, 0, 0)
  date.setDate(date.getDate() - day)
  return date
}

function sameDay(left: Date, right: Date) {
  return left.getFullYear() === right.getFullYear()
    && left.getMonth() === right.getMonth()
    && left.getDate() === right.getDate()
}

function formatWeekRange(start: Date) {
  const end = new Date(start)
  end.setDate(end.getDate() + 6)
  const format = new Intl.DateTimeFormat('pt-BR', { day: 'numeric', month: 'short', year: 'numeric' })
  return `${format.format(start)} — ${format.format(end)}`.replace(/ de /g, ' de ')
}

type Appointment = {
  id: string
  patientId: string
  patientName: string
  startsAt: string
  endsAt: string
  isCancelled: boolean
  createdAt: string
  updatedAt: string
}

type AppointmentForm = { patientId: string; startsAt: string; endsAt: string }

function toLocalDateTimeValue(value: Date) {
  const pad = (part: number) => String(part).padStart(2, '0')
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}T${pad(value.getHours())}:${pad(value.getMinutes())}`
}

function newAppointmentForm(): AppointmentForm {
  const startsAt = new Date()
  startsAt.setHours(startsAt.getHours() + 1, 0, 0, 0)
  const endsAt = new Date(startsAt.getTime() + 50 * 60 * 1000)
  return {
    patientId: '',
    startsAt: toLocalDateTimeValue(startsAt),
    endsAt: toLocalDateTimeValue(endsAt),
  }
}

export default function Appointments({
  openCreateOnMount = false,
  onCreateRequestHandled,
}: {
  openCreateOnMount?: boolean
  onCreateRequestHandled: () => void
}) {
  const [appointmentPatients, setAppointmentPatients] = useState<Patient[] | null>(null)
  const createRequestHandled = useRef(false)
  const [appointments, setAppointments] = useState<Appointment[]>([])
  const [includeCancelled, setIncludeCancelled] = useState(false)
  const [editingAppointment, setEditingAppointment] = useState<Appointment | null>(null)
  const [appointmentForm, setAppointmentForm] = useState<AppointmentForm>(newAppointmentForm)
  const [showAppointmentForm, setShowAppointmentForm] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [weekStart, setWeekStart] = useState(() => startOfWeek(new Date()))

  const loadAppointments = useCallback(async () => {
    const query = new URLSearchParams({ includeCancelled: String(includeCancelled) })
    setAppointments(await api<Appointment[]>(`/api/appointments?${query}`))
  }, [includeCancelled])

  const loadAppointmentPatients = useCallback(async () => {
    setAppointmentPatients(await api<Patient[]>('/api/patients?includeArchived=false'))
  }, [])

  useEffect(() => {
    Promise.all([loadAppointments(), loadAppointmentPatients()])
      .catch((reason: Error) => setError(reason.message))
  }, [loadAppointments, loadAppointmentPatients])

  useEffect(() => {
    if (!openCreateOnMount || appointmentPatients === null || createRequestHandled.current) return
    createRequestHandled.current = true
    startCreateAppointment()
    onCreateRequestHandled()
  }, [appointmentPatients, openCreateOnMount, onCreateRequestHandled])

  function startCreateAppointment() {
    const activePatients = (appointmentPatients ?? []).filter((patient) => !patient.isArchived)
    if (activePatients.length === 0) {
      setError('Cadastre um paciente ativo antes de criar um compromisso.')
      return
    }

    setEditingAppointment(null)
    setAppointmentForm({ ...newAppointmentForm(), patientId: activePatients[0].id })
    setError('')
    setShowAppointmentForm(true)
  }

  function startEditAppointment(appointment: Appointment) {
    setEditingAppointment(appointment)
    setAppointmentForm({
      patientId: appointment.patientId,
      startsAt: toLocalDateTimeValue(new Date(appointment.startsAt)),
      endsAt: toLocalDateTimeValue(new Date(appointment.endsAt)),
    })
    setError('')
    setShowAppointmentForm(true)
  }

  async function saveAppointment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')
    const startsAt = new Date(appointmentForm.startsAt)
    const endsAt = new Date(appointmentForm.endsAt)

    if (Number.isNaN(startsAt.getTime()) || Number.isNaN(endsAt.getTime()) || endsAt <= startsAt) {
      setError('O término deve ser posterior ao início do compromisso.')
      return
    }

    try {
      const saved = await api<Appointment>(
        editingAppointment ? `/api/appointments/${editingAppointment.id}` : '/api/appointments',
        {
          method: editingAppointment ? 'PUT' : 'POST',
          body: JSON.stringify({
            patientId: appointmentForm.patientId,
            startsAt: startsAt.toISOString(),
            endsAt: endsAt.toISOString(),
          }),
        },
      )
      setShowAppointmentForm(false)
      setNotice(editingAppointment ? 'Compromisso atualizado.' : 'Compromisso agendado.')
      setAppointments((current) => [
        ...current.filter((appointment) => appointment.id !== saved.id),
        saved,
      ].sort((left, right) => new Date(left.startsAt).getTime() - new Date(right.startsAt).getTime()))
    } catch (reason) {
      setError((reason as Error).message)
    }
  }

  async function cancelAppointment(appointment: Appointment) {
    if (!window.confirm(`Cancelar o compromisso de ${appointment.patientName}?`)) return
    setError('')

    try {
      await api<Appointment>(`/api/appointments/${appointment.id}/cancel`, { method: 'POST' })
      setNotice('Compromisso cancelado.')
      await loadAppointments()
    } catch (reason) {
      setError((reason as Error).message)
    }
  }

  const weekDays = Array.from({ length: 7 }, (_, index) => {
    const date = new Date(weekStart)
    date.setDate(date.getDate() + index)
    return date
  })
  const weekAppointments = appointments.filter((appointment) => {
    if (appointment.isCancelled && !includeCancelled) return false
    const date = new Date(appointment.startsAt)
    return date >= weekStart && date < new Date(weekStart.getTime() + 7 * 24 * 60 * 60 * 1000)
  })
  const activeWeekAppointments = weekAppointments.filter((appointment) => !appointment.isCancelled)
  const dayLabels = ['DOM', 'SEG', 'TER', 'QUA', 'QUI', 'SEX', 'SÁB']

  return (
    <>
      <div className="page-heading">
        <div>
          <p className="eyebrow">CONSULTÓRIO</p>
          <h1>Agenda</h1>
          <p className="agenda-summary">{activeWeekAppointments.length} sessões nesta semana · R$ 0 previstos</p>
        </div>
        <div className="agenda-heading-actions">
          <div className="week-picker">
            <button type="button" onClick={() => setWeekStart((current) => new Date(current.getTime() - 7 * 24 * 60 * 60 * 1000))} aria-label="Semana anterior">
              <FontAwesomeIcon icon={faChevronLeft} />
            </button>
            <span>{formatWeekRange(weekStart)}</span>
            <button type="button" onClick={() => setWeekStart((current) => new Date(current.getTime() + 7 * 24 * 60 * 60 * 1000))} aria-label="Próxima semana">
              <FontAwesomeIcon icon={faChevronRight} />
            </button>
          </div>
          <button className="button primary" onClick={startCreateAppointment}>
            <FontAwesomeIcon icon={faPlus} /> Nova sessão
          </button>
        </div>
      </div>
      {notice && (
        <div className="notice" role="status">
          {notice}
          <button onClick={() => setNotice('')} aria-label="Fechar aviso">×</button>
        </div>
      )}
      {error && (
        <div className="error-banner" role="alert">
          {error}
          <button onClick={() => setError('')} aria-label="Fechar erro">×</button>
        </div>
      )}
      <section className="agenda-week" aria-label="Agenda semanal">
        {weekDays.map((day, index) => {
          const dayAppointments = weekAppointments.filter((appointment) => sameDay(new Date(appointment.startsAt), day))
          const isToday = sameDay(day, new Date())
          return (
            <div className={`agenda-day ${isToday ? 'today' : ''}`} key={day.toISOString()}>
              <div className="agenda-day-header">
                <span>{dayLabels[index]} <strong>{day.getDate()}</strong></span>
                <button type="button" onClick={startCreateAppointment} aria-label={`Nova sessão em ${day.getDate()}`}><FontAwesomeIcon icon={faPlus} /></button>
              </div>
              <div className="agenda-day-body">
                {dayAppointments.length === 0 ? <span className="agenda-free">Livre</span> : dayAppointments.map((appointment) => (
                  <button className={`agenda-session ${appointment.isCancelled ? 'cancelled' : ''}`} key={appointment.id} onClick={() => startEditAppointment(appointment)}>
                    <span className="session-time"><i />{new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' }).format(new Date(appointment.startsAt))}</span>
                    <strong>{appointment.patientName}</strong>
                    <small>{appointment.isCancelled ? 'Cancelada' : 'Presencial'} · {Math.round((new Date(appointment.endsAt).getTime() - new Date(appointment.startsAt).getTime()) / 60000)}min</small>
                  </button>
                ))}
              </div>
            </div>
          )
        })}
      </section>
      <div className="agenda-footer">
        <div className="agenda-legend">
          <span><i className="present" /> Compareceu</span><span><i className="confirmed" /> Confirmada</span><span><i className="scheduled" /> Agendada</span><span><i className="missed" /> Faltou</span><span><i className="cancelled" /> Cancelada</span>
        </div>
        <label className="archive-toggle"><input type="checkbox" checked={includeCancelled} onChange={(event) => setIncludeCancelled(event.target.checked)} /> Mostrar cancelados</label>
        <span className="agenda-hint"><FontAwesomeIcon icon={faCalendarDays} /> Clique em uma sessão para editar</span>
      </div>
      <p className="privacy-note">
        <span><FontAwesomeIcon icon={faCircleInfo} /></span>
        {' '}Dados desta demonstração são sintéticos. Não use informações reais de pacientes.
      </p>

      {showAppointmentForm && (
        <div
          className="modal-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) setShowAppointmentForm(false)
          }}
        >
          <section
            className="modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="appointment-form-title"
          >
            <button
              className="modal-close"
              onClick={() => setShowAppointmentForm(false)}
              aria-label="Fechar"
            >
              ×
            </button>
            <p className="eyebrow">AGENDA</p>
            <h2 id="appointment-form-title">
              {editingAppointment ? 'Editar compromisso' : 'Novo compromisso'}
            </h2>
            <p className="muted">Escolha o paciente e o intervalo do atendimento.</p>
            <form className="form-stack" onSubmit={saveAppointment}>
              <label>
                Paciente <span className="required">*</span>
                <select
                  value={appointmentForm.patientId}
                  onChange={(event) => setAppointmentForm({
                    ...appointmentForm,
                    patientId: event.target.value,
                  })}
                  required
                >
                  {(appointmentPatients ?? [])
                    .filter((patient) => !patient.isArchived)
                    .map((patient) => (
                      <option key={patient.id} value={patient.id}>{patient.name}</option>
                    ))}
                </select>
              </label>
              <label>
                Início <span className="required">*</span>
                <input
                  type="datetime-local"
                  value={appointmentForm.startsAt}
                  onChange={(event) => setAppointmentForm({
                    ...appointmentForm,
                    startsAt: event.target.value,
                  })}
                  required
                />
              </label>
              <label>
                Término <span className="required">*</span>
                <input
                  type="datetime-local"
                  value={appointmentForm.endsAt}
                  onChange={(event) => setAppointmentForm({
                    ...appointmentForm,
                    endsAt: event.target.value,
                  })}
                  required
                />
              </label>
              {error && <p className="error" role="alert">{error}</p>}
              <div className="modal-actions">
                  {editingAppointment && !editingAppointment.isCancelled && (
                    <button
                      type="button"
                      className="text-button archive-action"
                      onClick={() => cancelAppointment(editingAppointment)}
                    >
                      Cancelar sessão
                    </button>
                  )}
                <button
                  type="button"
                  className="button secondary"
                  onClick={() => setShowAppointmentForm(false)}
                >
                  Fechar
                </button>
                <button className="button primary" type="submit">
                  {editingAppointment ? 'Salvar alterações' : 'Agendar'}
                </button>
              </div>
            </form>
          </section>
        </div>
      )}
    </>
  )
}
