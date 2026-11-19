import { FormEvent, useCallback, useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowLeft, faCalendarDays, faCirclePlus, faListCheck, faNotesMedical } from '@fortawesome/free-solid-svg-icons'
import { api } from '../../api'
import NumericText from '../../NumericText'
import './PatientDetails.css'

type Patient = {
  id: string
  name: string
  contact: string | null
  phone: string | null
  email: string | null
  isArchived: boolean
  createdAt: string
  updatedAt: string
}

type Session = {
  id: string
  sessionDate: string
  note: string
  createdAt: string
  updatedAt: string
}

type SessionVersion = {
  version: number
  sessionDate: string
  note: string
  changedAt: string
  changedBy: string
}

type PatientTask = {
  id: string
  description: string
  dueDate: string | null
  isCompleted: boolean
  createdAt: string
  updatedAt: string
}

type Appointment = {
  id: string
  startsAt: string
  endsAt: string
}

type SessionForm = { sessionDate: string; note: string }
type TaskForm = { description: string; dueDate: string; isCompleted: boolean }

const emptyTaskForm: TaskForm = { description: '', dueDate: '', isCompleted: false }

function todayValue() {
  const now = new Date()
  const pad = (part: number) => String(part).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'long' })
    .format(new Date(`${value}T00:00:00`))
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'medium', timeStyle: 'short' })
    .format(new Date(value))
}

function getErrorMessage(reason: unknown) {
  return reason instanceof Error ? reason.message : 'Não foi possível concluir a operação.'
}

export default function PatientDetails({
  patientId,
  onBack,
}: {
  patientId: string
  onBack: () => void
}) {
  const [patient, setPatient] = useState<Patient | null>(null)
  const [sessions, setSessions] = useState<Session[]>([])
  const [tasks, setTasks] = useState<PatientTask[]>([])
  const [nextAppointment, setNextAppointment] = useState<Appointment | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [editingSession, setEditingSession] = useState<Session | null>(null)
  const [sessionForm, setSessionForm] = useState<SessionForm>({ sessionDate: todayValue(), note: '' })
  const [showSessionForm, setShowSessionForm] = useState(false)
  const [editingTask, setEditingTask] = useState<PatientTask | null>(null)
  const [taskForm, setTaskForm] = useState<TaskForm>(emptyTaskForm)
  const [showTaskForm, setShowTaskForm] = useState(false)
  const [expandedSessionId, setExpandedSessionId] = useState<string | null>(null)
  const [versionsBySession, setVersionsBySession] = useState<Record<string, SessionVersion[]>>({})
  const [loadingVersions, setLoadingVersions] = useState<Record<string, boolean>>({})

  const loadDetails = useCallback(async () => {
    setLoading(true)
    setError('')

    try {
      const [loadedPatient, loadedSessions, loadedTasks, appointment] = await Promise.all([
        api<Patient>(`/api/patients/${patientId}`),
        api<Session[]>(`/api/patients/${patientId}/sessions`),
        api<PatientTask[]>(`/api/patients/${patientId}/tasks?includeCompleted=true`),
        api<Appointment | null>(`/api/patients/${patientId}/appointments/next`),
      ])
      setPatient(loadedPatient)
      setSessions(loadedSessions)
      setTasks(loadedTasks)
      setNextAppointment(appointment)
    } catch (reason) {
      setError(getErrorMessage(reason))
    } finally {
      setLoading(false)
    }
  }, [patientId])

  useEffect(() => {
    void loadDetails()
  }, [loadDetails])

  function startCreateSession() {
    setEditingSession(null)
    setSessionForm({ sessionDate: todayValue(), note: '' })
    setError('')
    setShowSessionForm(true)
  }

  function startEditSession(session: Session) {
    setEditingSession(session)
    setSessionForm({ sessionDate: session.sessionDate, note: session.note })
    setError('')
    setShowSessionForm(true)
  }

  async function saveSession(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')

    try {
      await api<Session>(
        editingSession
          ? `/api/patients/${patientId}/sessions/${editingSession.id}`
          : `/api/patients/${patientId}/sessions`,
        {
          method: editingSession ? 'PUT' : 'POST',
          body: JSON.stringify(sessionForm),
        },
      )
      setShowSessionForm(false)
      setNotice(editingSession ? 'Registro atualizado; a versão anterior foi preservada.' : 'Sessão registrada.')
      await loadDetails()
      setExpandedSessionId(null)
      setVersionsBySession({})
    } catch (reason) {
      setError(getErrorMessage(reason))
    }
  }

  async function toggleVersions(sessionId: string) {
    if (expandedSessionId === sessionId) {
      setExpandedSessionId(null)
      return
    }

    setExpandedSessionId(sessionId)
    if (versionsBySession[sessionId]) return
    setLoadingVersions((current) => ({ ...current, [sessionId]: true }))
    setError('')

    try {
      const loadedVersions = await api<SessionVersion[]>(
        `/api/patients/${patientId}/sessions/${sessionId}/versions`,
      )
      setVersionsBySession((current) => ({ ...current, [sessionId]: loadedVersions }))
    } catch (reason) {
      setError(getErrorMessage(reason))
    } finally {
      setLoadingVersions((current) => ({ ...current, [sessionId]: false }))
    }
  }

  function startCreateTask() {
    setEditingTask(null)
    setTaskForm(emptyTaskForm)
    setError('')
    setShowTaskForm(true
    )
  }

  function startEditTask(task: PatientTask) {
    setEditingTask(task)
    setTaskForm({
      description: task.description,
      dueDate: task.dueDate ?? '',
      isCompleted: task.isCompleted,
    })
    setError('')
    setShowTaskForm(true)
  }

  async function saveTask(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')

    try {
      await api<PatientTask>(
        editingTask
          ? `/api/patients/${patientId}/tasks/${editingTask.id}`
          : `/api/patients/${patientId}/tasks`,
        {
          method: editingTask ? 'PUT' : 'POST',
          body: JSON.stringify({
            description: taskForm.description,
            dueDate: taskForm.dueDate || null,
            isCompleted: taskForm.isCompleted,
          }),
        },
      )
      setShowTaskForm(false)
      setNotice(editingTask ? 'Pendência atualizada.' : 'Pendência criada.')
      await loadDetails()
    } catch (reason) {
      setError(getErrorMessage(reason))
    }
  }

  async function completeTask(task: PatientTask) {
    setError('')

    try {
      await api<PatientTask>(`/api/patients/${patientId}/tasks/${task.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          description: task.description,
          dueDate: task.dueDate,
          isCompleted: true,
        }),
      })
      setNotice('Pendência concluída.')
      await loadDetails()
    } catch (reason) {
      setError(getErrorMessage(reason))
    }
  }

  if (loading) return <div className="loading patient-detail-loading">Carregando paciente…</div>

  if (!patient) {
    return (
      <section className="patient-detail">
        <button className="button secondary back-button" onClick={onBack}>
          <FontAwesomeIcon icon={faArrowLeft} /> Voltar aos pacientes
        </button>
        {error && <div className="error-banner" role="alert">{error}</div>}
      </section>
    )
  }

  const openTasks = tasks.filter((task) => !task.isCompleted)
  const latestSession = sessions[0]

  return (
    <section className="patient-detail">
      <button className="text-button back-button" onClick={onBack}>
        <FontAwesomeIcon icon={faArrowLeft} /> Voltar aos pacientes
      </button>
      <div className="patient-detail-heading">
        <div>
          <p className="eyebrow">PRONTUÁRIO DO PACIENTE</p>
          <h1>{patient.name}</h1>
          <p className="muted">
            {[patient.phone, patient.email, patient.contact].filter(Boolean).join(' · ') || 'Sem contato informado'}
            {patient.isArchived && <span className="status-pill patient-archived-pill">Arquivado</span>}
          </p>
        </div>
        <div className="detail-heading-actions">
          <button className="button secondary" onClick={startCreateTask}>
            <FontAwesomeIcon icon={faCirclePlus} /> Nova pendência
          </button>
          <button className="button primary" onClick={startCreateSession}>
            <FontAwesomeIcon icon={faNotesMedical} /> Registrar sessão
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

      <div className="overview-heading">
        <h2>Visão geral</h2>
        <p>O contexto necessário para acompanhar seu atendimento.</p>
      </div>
      <section className="patient-overview" aria-label="Visão geral do paciente">
        <article className="overview-card">
          <div className="overview-card-heading">
            <span className="overview-icon"><FontAwesomeIcon icon={faNotesMedical} /></span>
            <span className="overview-label">Última sessão</span>
          </div>
          {latestSession ? (
            <>
              <strong><NumericText value={formatDate(latestSession.sessionDate)} /></strong>
              <button className="text-button" onClick={() => startEditSession(latestSession)}>
                Abrir registro
              </button>
            </>
          ) : (
            <p>Nenhuma sessão registrada.</p>
          )}
        </article>
        <article className="overview-card">
          <div className="overview-card-heading">
            <span className="overview-icon"><FontAwesomeIcon icon={faCalendarDays} /></span>
            <span className="overview-label">Próximo compromisso</span>
          </div>
          {nextAppointment ? (
            <>
              <strong><NumericText value={formatDateTime(nextAppointment.startsAt)} /></strong>
              <span>
                até <NumericText value={new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' })
                  .format(new Date(nextAppointment.endsAt))} />
              </span>
            </>
          ) : (
            <p>Nenhum compromisso futuro agendado.</p>
          )}
        </article>
        <article className="overview-card">
          <div className="overview-card-heading">
            <span className="overview-icon"><FontAwesomeIcon icon={faListCheck} /></span>
            <span className="overview-label">Pendências em aberto</span>
          </div>
          {openTasks.length === 0 ? (
            <p>Nenhuma pendência em aberto.</p>
          ) : (
            <>
              <strong><NumericText value={openTasks.length} /> {openTasks.length === 1 ? 'pendência' : 'pendências'}</strong>
              <ul className="overview-task-list">
                {openTasks.slice(0, 3).map((task) => (
                  <li key={task.id}>
                    {task.description}
                    {task.dueDate && <span> · Prazo: <NumericText value={formatDate(task.dueDate)} /></span>}
                  </li>
                ))}
              </ul>
              <a className="text-button" href="#patient-tasks">Ver pendências</a>
            </>
          )}
        </article>
      </section>

      <section className="directory-card detail-section" id="session-history">
        <div className="directory-header">
          <div>
            <h2>Histórico de sessões</h2>
            <p>Registros em ordem cronológica, do mais recente ao mais antigo.</p>
          </div>
          <button className="button secondary" onClick={startCreateSession}>Registrar sessão</button>
        </div>
        {sessions.length === 0 ? (
          <div className="empty-state detail-empty-state">
            <span className="empty-icon"><FontAwesomeIcon icon={faNotesMedical} /></span>
            <strong>Nenhuma sessão registrada</strong>
            <p>Registre uma sessão para iniciar o histórico deste paciente.</p>
          </div>
        ) : (
          <div className="detail-record-list">
            {sessions.map((session) => (
              <article className="detail-record" key={session.id}>
                <div className="detail-record-heading">
                  <div>
                    <strong><NumericText value={formatDate(session.sessionDate)} /></strong>
                    <span>Atualizado em <NumericText value={formatDateTime(session.updatedAt)} /></span>
                  </div>
                  <div className="row-actions">
                    <button className="text-button" onClick={() => startEditSession(session)}>
                      Abrir / editar
                    </button>
                    <button className="text-button" onClick={() => toggleVersions(session.id)}>
                      {expandedSessionId === session.id ? 'Ocultar versões' : 'Ver versões'}
                    </button>
                  </div>
                </div>
                <p className="record-note">{session.note}</p>
                {expandedSessionId === session.id && (
                  <div className="version-list">
                    <h3>Versões do registro</h3>
                    {loadingVersions[session.id] ? (
                      <p className="muted">Carregando versões…</p>
                    ) : (versionsBySession[session.id] ?? []).map((version) => (
                      <article className="version-item" key={version.version}>
                        <div>
                          <strong>Versão <NumericText value={version.version} /> · <NumericText value={formatDate(version.sessionDate)} /></strong>
                          <span><NumericText value={formatDateTime(version.changedAt)} /> · Alterado por {version.changedBy}</span>
                        </div>
                        <p className="record-note">{version.note}</p>
                      </article>
                    ))}
                  </div>
                )}
              </article>
            ))}
          </div>
        )}
      </section>

      <section className="directory-card detail-section" id="patient-tasks">
        <div className="directory-header">
          <div>
            <h2>Pendências</h2>
            <p>Itens em aberto e concluídos deste paciente.</p>
          </div>
          <button className="button secondary" onClick={startCreateTask}>Nova pendência</button>
        </div>
        {tasks.length === 0 ? (
          <div className="empty-state detail-empty-state">
            <strong>Nenhuma pendência cadastrada</strong>
            <p>Adicione um item para acompanhar algo a retomar ou concluir.</p>
          </div>
        ) : (
          <div className="detail-record-list">
            {tasks.map((task) => (
              <article className={`detail-record task-record ${task.isCompleted ? 'task-completed' : ''}`} key={task.id}>
                <div className="task-description">
                  <strong>{task.description}</strong>
                  <span>
                    {task.isCompleted ? 'Concluída' : 'Em aberto'}
                    {task.dueDate && <> · Prazo: <NumericText value={formatDate(task.dueDate)} /></>}
                  </span>
                </div>
                <div className="row-actions">
                  {!task.isCompleted && (
                    <button className="text-button" onClick={() => completeTask(task)}>Concluir</button>
                  )}
                  <button className="text-button" onClick={() => startEditTask(task)}>Editar</button>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>

      <p className="privacy-note">
        <span><FontAwesomeIcon icon={faCalendarDays} /></span>
        {' '}Use somente dados sintéticos nesta demonstração. Não registre informações reais de pacientes.
      </p>

      {showSessionForm && (
        <div
          className="modal-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) setShowSessionForm(false)
          }}
        >
          <section className="modal clinical-modal" role="dialog" aria-modal="true" aria-labelledby="session-form-title">
            <button className="modal-close" onClick={() => setShowSessionForm(false)} aria-label="Fechar">×</button>
            <p className="eyebrow">HISTÓRICO DE SESSÕES</p>
            <h2 id="session-form-title">{editingSession ? 'Editar registro' : 'Registrar sessão'}</h2>
            <form className="form-stack" onSubmit={saveSession}>
              <label>
                Data da sessão <span className="required">*</span>
                <input
                  type="date"
                  value={sessionForm.sessionDate}
                  onChange={(event) => setSessionForm({ ...sessionForm, sessionDate: event.target.value })}
                  required
                />
              </label>
              <label>
                Anotação <span className="required">*</span>
                <textarea
                  rows={8}
                  maxLength={20000}
                  value={sessionForm.note}
                  onChange={(event) => setSessionForm({ ...sessionForm, note: event.target.value })}
                  required
                />
              </label>
              {error && <p className="error" role="alert">{error}</p>}
              <div className="modal-actions">
                <button type="button" className="button secondary" onClick={() => setShowSessionForm(false)}>
                  Cancelar
                </button>
                <button className="button primary" type="submit">Salvar registro</button>
              </div>
            </form>
          </section>
        </div>
      )}

      {showTaskForm && (
        <div
          className="modal-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) setShowTaskForm(false)
          }}
        >
          <section className="modal" role="dialog" aria-modal="true" aria-labelledby="task-form-title">
            <button className="modal-close" onClick={() => setShowTaskForm(false)} aria-label="Fechar">×</button>
            <p className="eyebrow">ACOMPANHAMENTO</p>
            <h2 id="task-form-title">{editingTask ? 'Editar pendência' : 'Nova pendência'}</h2>
            <form className="form-stack" onSubmit={saveTask}>
              <label>
                Descrição <span className="required">*</span>
                <textarea
                  rows={4}
                  maxLength={2000}
                  value={taskForm.description}
                  onChange={(event) => setTaskForm({ ...taskForm, description: event.target.value })}
                  required
                />
              </label>
              <label>
                Prazo <span className="optional">(opcional)</span>
                <input
                  type="date"
                  value={taskForm.dueDate}
                  onChange={(event) => setTaskForm({ ...taskForm, dueDate: event.target.value })}
                />
              </label>
              {editingTask && (
                <label className="task-status-control">
                  <input
                    type="checkbox"
                    checked={taskForm.isCompleted}
                    onChange={(event) => setTaskForm({ ...taskForm, isCompleted: event.target.checked })}
                  />
                  Concluída
                </label>
              )}
              {error && <p className="error" role="alert">{error}</p>}
              <div className="modal-actions">
                <button type="button" className="button secondary" onClick={() => setShowTaskForm(false)}>
                  Cancelar
                </button>
                <button className="button primary" type="submit">Salvar pendência</button>
              </div>
            </form>
          </section>
        </div>
      )}
    </section>
  )
}
