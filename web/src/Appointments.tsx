import { FormEvent, useCallback, useEffect, useRef, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCalendarDays, faChevronLeft, faChevronRight, faCircleInfo, faPlus, faReceipt, faTrashCan } from '@fortawesome/free-solid-svg-icons'
import { api } from './api'
import NumericText from './NumericText'

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
  amount: number
  modality: 'in_person' | 'online'
  status: 'scheduled' | 'confirmed' | 'attended' | 'missed' | 'cancelled'
  notes: string
  createdAt: string
  updatedAt: string
}

type AppointmentForm = {
  patientId: string
  date: string
  time: string
  duration: string
  amount: string
  modality: 'in_person' | 'online'
  status: Appointment['status']
  notes: string
}

function toLocalDateTimeValue(value: Date) {
  const pad = (part: number) => String(part).padStart(2, '0')
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}T${pad(value.getHours())}:${pad(value.getMinutes())}`
}

function newAppointmentForm(): AppointmentForm {
  const startsAt = new Date()
  startsAt.setHours(startsAt.getHours() + 1, 0, 0, 0)
  const startsAtValue = toLocalDateTimeValue(startsAt)
  return {
    patientId: '',
    date: startsAtValue.slice(0, 10),
    time: startsAtValue.slice(11, 16),
    duration: '50',
    amount: '0',
    modality: 'in_person',
    status: 'scheduled',
    notes: '',
  }
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
}

function isAppointmentStatus(value: string): value is Appointment['status'] {
  return value === 'scheduled' || value === 'confirmed' || value === 'attended'
    || value === 'missed' || value === 'cancelled'
}

function printAppointmentReceipt(appointment: Appointment) {
  const receiptWindow = window.open('', '_blank')
  if (!receiptWindow) return false

  const document = receiptWindow.document
  const style = document.createElement('style')
  style.textContent = 'body{font-family:Arial,sans-serif;color:#2a3c5b;padding:32px;max-width:640px;margin:auto}h1{font-size:24px}p{margin:8px 0}.line{border-top:1px solid #dfe7e2;margin:20px 0}.total{font-size:20px;font-weight:bold}'
  const content = document.createElement('main')
  const title = document.createElement('h1')
  title.textContent = 'Recibo de atendimento'
  content.append(title)

  const start = new Date(appointment.startsAt)
  const end = new Date(appointment.endsAt)
  const details = [
    `Paciente: ${appointment.patientName}`,
    `Data: ${new Intl.DateTimeFormat('pt-BR', { dateStyle: 'long' }).format(start)}`,
    `Horário: ${new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' }).format(start)} às ${new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' }).format(end)}`,
    `Modalidade: ${appointment.modality === 'online' ? 'Online' : 'Presencial'}`,
  ]
  details.forEach((detail) => {
    const paragraph = document.createElement('p')
    paragraph.textContent = detail
    content.append(paragraph)
  })

  const divider = document.createElement('div')
  divider.className = 'line'
  content.append(divider)
  const total = document.createElement('p')
  total.className = 'total'
  total.textContent = `Valor: ${formatCurrency(appointment.amount)}`
  content.append(total)
  document.body.replaceChildren(style, content)
  document.title = 'Recibo de atendimento'
  document.close()
  receiptWindow.focus()
  window.setTimeout(() => receiptWindow.print(), 250)
  return true
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
  const [savingAppointment, setSavingAppointment] = useState(false)

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
    const startsAt = new Date(appointment.startsAt)
    const startsAtValue = toLocalDateTimeValue(startsAt)
    setAppointmentForm({
      patientId: appointment.patientId,
      date: startsAtValue.slice(0, 10),
      time: startsAtValue.slice(11, 16),
      duration: String(Math.round((new Date(appointment.endsAt).getTime() - startsAt.getTime()) / 60000)),
      amount: String(appointment.amount),
      modality: appointment.modality,
      status: appointment.status,
      notes: appointment.notes,
    })
    setError('')
    setShowAppointmentForm(true)
  }

  async function saveAppointment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')
    const startsAt = new Date(`${appointmentForm.date}T${appointmentForm.time}`)
    const duration = Number(appointmentForm.duration)
    const amount = Number(appointmentForm.amount)
    const endsAt = new Date(startsAt.getTime() + duration * 60 * 1000)

    if (Number.isNaN(startsAt.getTime()) || !Number.isInteger(duration) || duration < 1 || duration > 1440) {
      setError('Informe uma data, horário e duração válidos (de 1 a 1440 minutos).')
      return
    }
    if (!Number.isFinite(amount) || amount < 0 || Math.round(amount * 100) !== amount * 100) {
      setError('Informe um valor válido, com até duas casas decimais.')
      return
    }

    setSavingAppointment(true)
    try {
      await api<Appointment>(
        editingAppointment ? `/api/appointments/${editingAppointment.id}` : '/api/appointments',
        {
          method: editingAppointment ? 'PUT' : 'POST',
          body: JSON.stringify({
            patientId: appointmentForm.patientId,
            startsAt: startsAt.toISOString(),
            endsAt: endsAt.toISOString(),
            amount,
            modality: appointmentForm.modality,
            status: appointmentForm.status,
            notes: appointmentForm.notes.trim(),
          }),
        },
      )
      setShowAppointmentForm(false)
      setNotice(editingAppointment ? 'Compromisso atualizado.' : 'Compromisso agendado.')
      await loadAppointments()
    } catch (reason) {
      setError((reason as Error).message)
    } finally {
      setSavingAppointment(false)
    }
  }

  async function deleteAppointment(appointment: Appointment) {
    if (!window.confirm(`Excluir o compromisso de ${appointment.patientName}? Esta ação não pode ser desfeita.`)) return
    setError('')
    try {
      await api<void>(`/api/appointments/${appointment.id}`, { method: 'DELETE' })
      setShowAppointmentForm(false)
      setNotice('Compromisso excluído.')
      await loadAppointments()
    } catch (reason) {
      setError((reason as Error).message)
    }
  }

  function createReceipt(appointment: Appointment) {
    if (!printAppointmentReceipt(appointment))
      setError('Permita a abertura de janelas pop-up para gerar o recibo.')
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
  const projectedIncome = activeWeekAppointments
    .filter((appointment) => appointment.status === 'scheduled' || appointment.status === 'confirmed')
    .reduce((sum, appointment) => sum + appointment.amount, 0)
  const dayLabels = ['DOM', 'SEG', 'TER', 'QUA', 'QUI', 'SEX', 'SÁB']

  return (
    <>
      <div className="page-heading">
        <div>
          <p className="eyebrow">CONSULTÓRIO</p>
          <h1>Agenda</h1>
          <p className="agenda-summary">
            <NumericText value={activeWeekAppointments.length} /> sessões nesta semana · <NumericText value={formatCurrency(projectedIncome)} /> previstos
          </p>
        </div>
        <div className="agenda-heading-actions">
          <div className="week-picker">
            <button type="button" onClick={() => setWeekStart((current) => new Date(current.getTime() - 7 * 24 * 60 * 60 * 1000))} aria-label="Semana anterior">
              <FontAwesomeIcon icon={faChevronLeft} />
            </button>
          <span><NumericText value={formatWeekRange(weekStart)} /></span>
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
                <span>{dayLabels[index]} <strong><NumericText value={day.getDate()} /></strong></span>
                <button type="button" onClick={startCreateAppointment} aria-label={`Nova sessão em ${day.getDate()}`}><FontAwesomeIcon icon={faPlus} /></button>
              </div>
              <div className="agenda-day-body">
                {dayAppointments.length === 0 ? <span className="agenda-free">Livre</span> : dayAppointments.map((appointment) => (
                  <button className={`agenda-session ${appointment.isCancelled ? 'cancelled' : ''} status-${appointment.status}`} key={appointment.id} onClick={() => startEditAppointment(appointment)}>
                    <span className="session-time"><i /><NumericText value={new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' }).format(new Date(appointment.startsAt))} /></span>
                    <strong>{appointment.patientName}</strong>
                    <small>
                      {appointment.isCancelled ? 'Cancelada' : appointment.modality === 'online' ? 'Online' : 'Presencial'}
                      {' · '}<NumericText value={Math.round((new Date(appointment.endsAt).getTime() - new Date(appointment.startsAt).getTime()) / 60000)} />min
                    </small>
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
            if (event.target === event.currentTarget && !savingAppointment) setShowAppointmentForm(false)
          }}
        >
          <section
            className="modal appointment-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="appointment-form-title"
          >
            <button
              className="modal-close"
              onClick={() => setShowAppointmentForm(false)}
              aria-label="Fechar"
              disabled={savingAppointment}
            >
              ×
            </button>
            <p className="eyebrow">AGENDA</p>
            <h2 id="appointment-form-title">
              {editingAppointment ? 'Editar compromisso' : 'Novo compromisso'}
            </h2>
            <p className="muted">Registre o atendimento e controle a presença do paciente.</p>
            <form className="form-stack" onSubmit={saveAppointment}>
              <div className="appointment-form-grid">
                <label className="appointment-form-wide">
                  Paciente <span className="required">*</span>
                  <select
                    value={appointmentForm.patientId}
                    onChange={(event) => setAppointmentForm({ ...appointmentForm, patientId: event.target.value })}
                    required
                  >
                    {(appointmentPatients ?? [])
                      .filter((patient) => !patient.isArchived)
                      .map((patient) => <option key={patient.id} value={patient.id}>{patient.name}</option>)}
                  </select>
                </label>
                <label>
                  Data <span className="required">*</span>
                  <input type="date" value={appointmentForm.date} onChange={(event) => setAppointmentForm({ ...appointmentForm, date: event.target.value })} required />
                </label>
                <label>
                  Horário <span className="required">*</span>
                  <input type="time" value={appointmentForm.time} onChange={(event) => setAppointmentForm({ ...appointmentForm, time: event.target.value })} required />
                </label>
                <label>
                  Duração (min) <span className="required">*</span>
                  <input type="number" min="1" max="1440" step="1" value={appointmentForm.duration} onChange={(event) => setAppointmentForm({ ...appointmentForm, duration: event.target.value })} required />
                </label>
                <label>
                  Valor (R$) <span className="required">*</span>
                  <input type="number" min="0" max="999999999.99" step="0.01" value={appointmentForm.amount} onChange={(event) => setAppointmentForm({ ...appointmentForm, amount: event.target.value })} required />
                </label>
                <label>
                  Modalidade <span className="required">*</span>
                  <select value={appointmentForm.modality} onChange={(event) => setAppointmentForm({ ...appointmentForm, modality: event.target.value === 'online' ? 'online' : 'in_person' })}>
                    <option value="in_person">Presencial</option>
                    <option value="online">Online</option>
                  </select>
                </label>
                <label>
                  Presença / status <span className="required">*</span>
                  <select value={appointmentForm.status} onChange={(event) => {
                    if (isAppointmentStatus(event.target.value))
                      setAppointmentForm({ ...appointmentForm, status: event.target.value })
                  }}>
                    <option value="scheduled">Agendada</option>
                    <option value="confirmed">Confirmada</option>
                    <option value="attended">Compareceu</option>
                    <option value="missed">Faltou</option>
                    {editingAppointment && <option value="cancelled">Cancelada</option>}
                  </select>
                </label>
                <label className="appointment-form-wide">
                  Observações <span className="optional">(opcional)</span>
                  <textarea maxLength={2000} placeholder="Anotações do agendamento (opcional)" value={appointmentForm.notes} onChange={(event) => setAppointmentForm({ ...appointmentForm, notes: event.target.value })} />
                </label>
              </div>
              {error && <p className="error" role="alert">{error}</p>}
              <div className="appointment-modal-actions">
                <div className="appointment-modal-tools">
                  {editingAppointment && (
                    <>
                      <button type="button" className="text-button archive-action" onClick={() => deleteAppointment(editingAppointment)} disabled={savingAppointment}>
                        <FontAwesomeIcon icon={faTrashCan} /> Excluir
                      </button>
                      <button type="button" className="text-button appointment-receipt-button" onClick={() => createReceipt(editingAppointment)}>
                        <FontAwesomeIcon icon={faReceipt} /> Recibo
                      </button>
                    </>
                  )}
                </div>
                <div className="modal-actions">
                  <button type="button" className="button secondary" onClick={() => setShowAppointmentForm(false)} disabled={savingAppointment}>Fechar</button>
                  <button className="button primary" type="submit" disabled={savingAppointment}>
                    {savingAppointment ? 'Salvando…' : editingAppointment ? 'Salvar alterações' : 'Agendar'}
                  </button>
                </div>
              </div>
            </form>
          </section>
        </div>
      )}
    </>
  )
}
