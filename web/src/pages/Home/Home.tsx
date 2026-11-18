import { useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowRight, faArrowUpRightFromSquare, faCalendarDays, faCircleDollarToSlot, faCircleInfo, faHospitalUser, faPlus, faWallet } from '@fortawesome/free-solid-svg-icons'
import { api } from '../../api'
import NumericText from '../../NumericText'
import './Home.css'

type Patient = { id: string }
type Appointment = {
  id: string
  patientName: string
  startsAt: string
  isCancelled: boolean
}
type FinancialEntry = { type: 'revenue' | 'expense'; status: 'paid' | 'pending'; amount: number }

type Overview = {
  patientCount: number
  appointmentsToday: number
  todayAppointments: Appointment[]
  receivedThisMonth: number
  receivableThisMonth: number
}

export default function Home({
  userName,
  onOpenPatients,
  onOpenAppointments,
  onCreateAppointment,
}: {
  userName: string
  onOpenPatients: () => void
  onOpenAppointments: () => void
  onCreateAppointment: () => void
}) {
  const [overview, setOverview] = useState<Overview | null>(null)
  const [error, setError] = useState('')
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let active = true
    setError('')
    setOverview(null)

    Promise.all([
      api<Patient[]>('/api/patients?includeArchived=false'),
      api<Appointment[]>('/api/appointments?includeCancelled=false'),
      api<FinancialEntry[]>(`/api/financial-entries?year=${new Date().getFullYear()}&month=${new Date().getMonth() + 1}`),
    ]).then(([patients, appointments, financialEntries]) => {
      if (!active) return
      const now = new Date()
      const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime()
      const startOfTomorrow = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1).getTime()
      const todayAppointments = appointments
        .filter((appointment) => {
          const startsAt = new Date(appointment.startsAt).getTime()
          return !appointment.isCancelled && startsAt >= startOfToday && startsAt < startOfTomorrow
        })
        .sort((left, right) => new Date(left.startsAt).getTime() - new Date(right.startsAt).getTime())
      setOverview({
        patientCount: patients.length,
        appointmentsToday: todayAppointments.length,
        todayAppointments,
        receivedThisMonth: financialEntries
          .filter((entry) => entry.type === 'revenue' && entry.status === 'paid')
          .reduce((sum, entry) => sum + entry.amount, 0),
        receivableThisMonth: financialEntries
          .filter((entry) => entry.type === 'revenue' && entry.status === 'pending')
          .reduce((sum, entry) => sum + entry.amount, 0),
      })
    }).catch((reason: unknown) => {
      if (active) setError(reason instanceof Error ? reason.message : 'Não foi possível carregar a visão geral.')
    })

    return () => { active = false }
  }, [attempt])

    const dataAtual = new Date().toLocaleDateString("pt-BR", {
        weekday: "long",
        day: "2-digit",
        month: "long",
    });

  return (
    <div className="home-page">
      <div>
        <div>
          <p className="eyebrow"><NumericText value={dataAtual} /></p>
          <h1 className="greetings">Olá, {userName}!</h1>
        </div>
      </div>

      <section className="home-section" aria-labelledby="home-overview-title">
        {error ? (
          <div className="error-banner" role="alert">
            <span>{error}</span>
            <button className="text-button" onClick={() => setAttempt((current) => current + 1)}>Tentar novamente</button>
          </div>
        ) : !overview ? (
          <p className="muted" role="status">Carregando visão geral…</p>
        ) : (
          <div className="home-overview">
            <article className="home-card">
              <div className="home-card-heading">
                <span>Consultas hoje</span>
                <span className="home-card-icon"><FontAwesomeIcon icon={faCalendarDays} /></span>
              </div>
              <strong><NumericText value={overview.appointmentsToday} /></strong>
              <span className="home-card-caption">Na agenda</span>
            </article>
            <article className="home-card">
              <div className="home-card-heading">
                <span>Pacientes ativos</span>
                <span className="home-card-icon"><FontAwesomeIcon icon={faHospitalUser} /></span>
              </div>
              <strong><NumericText value={overview.patientCount} /></strong>
              <span className="home-card-caption">Cadastros ativos</span>
            </article>
            <article className="home-card">
              <div className="home-card-heading">
                <span>Recebidos no mês</span>
                <span className="home-card-icon"><FontAwesomeIcon icon={faWallet} /></span>
              </div>
              <strong><NumericText value={new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(overview.receivedThisMonth)} /></strong>
              <span className="home-card-caption">Neste mês</span>
            </article>
            <article className="home-card">
              <div className="home-card-heading">
                <span>A receber</span>
                <span className="home-card-icon"><FontAwesomeIcon icon={faCircleDollarToSlot} /></span>
              </div>
              <strong><NumericText value={new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(overview.receivableThisMonth)} /></strong>
              <span className="home-card-caption">Pagamentos pendentes</span>
            </article>
          </div>
        )}
      </section>

      {overview && (
        <div className="home-dashboard-grid">
          <section className="directory-card home-agenda" aria-labelledby="home-agenda-title">
            <div className="directory-header home-agenda-header">
              <div>
                <h2 id="home-agenda-title">Agenda de hoje</h2>
                <p>Seus atendimentos e horários do dia.</p>
              </div>
              <button className="text-button home-agenda-link" onClick={onOpenAppointments}>
                Ver agenda <FontAwesomeIcon icon={faArrowUpRightFromSquare} />
              </button>
            </div>
            {overview.todayAppointments.length === 0 ? (
              <div className="home-agenda-empty">
                <span className="home-agenda-empty-icon"><FontAwesomeIcon icon={faCalendarDays} /></span>
                <strong>Dia livre</strong>
                <p>Nenhuma sessão agendada para hoje.</p>
                <button className="button primary" onClick={onCreateAppointment}>
                  <FontAwesomeIcon icon={faPlus} /> Agendar sessão
                </button>
              </div>
            ) : (
              <ul className="home-agenda-list">
                {overview.todayAppointments.map((appointment) => (
                  <li key={appointment.id}>
                    <time dateTime={appointment.startsAt}>
                      <NumericText value={new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' })
                        .format(new Date(appointment.startsAt))} />
                    </time>
                    {/* <time dateTime={appointment.duration}>
                        {new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' })
                            .format(new Date(appointment.startsAt))}
                    </time> */}
                    <strong>{appointment.patientName}</strong>
                    <span>Sessão agendada</span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <aside className="home-dashboard-aside">
            <section className="directory-card home-side-card" aria-labelledby="home-access-title">
              <div className="directory-header home-side-header">
                <div>
                  <h2 id="home-access-title">Acesso rápido</h2>
                  <p>Atalhos do consultório.</p>
                </div>
              </div>
              <div className="home-access">
                <button className="home-access-card" onClick={onOpenPatients}>
                  <span className="summary-icon"><FontAwesomeIcon icon={faHospitalUser} /></span>
                  <span><strong>Pacientes</strong><small>Cadastros e registros.</small></span>
                  <FontAwesomeIcon className="home-arrow" icon={faArrowRight} />
                </button>
                <button className="home-access-card" onClick={onOpenAppointments}>
                  <span className="summary-icon"><FontAwesomeIcon icon={faCalendarDays} /></span>
                  <span><strong>Agenda</strong><small>Organize seus compromissos.</small></span>
                  <FontAwesomeIcon className="home-arrow" icon={faArrowRight} />
                </button>
              </div>
            </section>

            {/* <section className="directory-card home-side-card" aria-labelledby="home-evolutions-title">
              <div className="directory-header home-side-header">
                <div>
                  <h2 id="home-evolutions-title">Últimas evoluções</h2>
                </div>
                <button className="text-button" onClick={onOpenPatients}>Ver pacientes</button>
              </div>
              <p className="home-side-empty">Consulte os registros clínicos nas fichas dos pacientes.</p>
            </section> */}
          </aside>
        </div>
      )}

      <p className="privacy-note">
        <span><FontAwesomeIcon icon={faCircleInfo} /></span>
        {' '}Dados desta demonstração são sintéticos. Não use informações reais de pacientes.
      </p>
    </div>
  )
}
