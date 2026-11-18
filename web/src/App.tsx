import { FormEvent, useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faCalendarDays,
  faHouse,
  faNotesMedical,
  faRightFromBracket,
  faWallet,
} from '@fortawesome/free-solid-svg-icons'
import Appointments from './pages/Appointments/Appointments'
import Finance from './pages/Finance/Finance'
import Home from './pages/Home/Home'
import Patients from './pages/Patients/Patients'
import { api } from './api'
import './App.css'

const logoUrl = new URL('../img/logotipo.png', import.meta.url).href

type User = { name: string; email: string }
type Section = 'home' | 'patients' | 'appointments' | 'finance'

export default function App() {
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState(true)
  const [section, setSection] = useState<Section>('home')
  const [createAppointmentOnOpen, setCreateAppointmentOnOpen] = useState(false)
  const [loginError, setLoginError] = useState('')

  useEffect(() => {
    api<User>('/api/auth/me')
      .then(setUser)
      .catch(() => setUser(null))
      .finally(() => setLoading(false))
  }, [])

  async function handleLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const values = new FormData(event.currentTarget)
    setLoginError('')

    try {
      const signedIn = await api<User>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({
          email: values.get('email'),
          password: values.get('password'),
        }),
      })
      setUser(signedIn)
    } catch {
      setLoginError('E-mail ou senha inválidos.')
    }
  }

  async function logout() {
    try {
      await api<void>('/api/auth/logout', { method: 'POST' })
    } finally {
      setUser(null)
      setSection('home')
    }
  }

  if (loading) return <main className="loading">Carregando workspace…</main>

  if (!user) {
    return (
      <main className="login-page">
        <section className="login-card">
          <div className="brand-mark">P</div>
          <p className="eyebrow">Aura Clínica · WORKSPACE</p>
          <h1>Um espaço mais organizado para sua rotina.</h1>
          <p className="muted">Entre para acessar seus pacientes.</p>
          <form onSubmit={handleLogin} className="form-stack">
            <label>
              E-mail
              <input name="email" type="email" autoComplete="username" required />
            </label>
            <label>
              Senha
              <input name="password" type="password" autoComplete="current-password" required />
            </label>
            {loginError && <p className="error" role="alert">{loginError}</p>}
            <button className="button primary full" type="submit">Entrar no workspace</button>
          </form>
          <p className="login-footnote">Ambiente de demonstração com dados sintéticos.</p>
        </section>
      </main>
    )
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a className="brand" href="#home" onClick={() => setSection('home')}>
          <span className="brand-mark small">
          <img src={logoUrl} alt="Logotipo Aura Clínica" />
          </span>
          <span>
            <span style={{ fontFamily: 'Outfit, sans-serif', fontWeight: 600, fontSize: '24px' }}>Aura </span>
            <span style={{ fontFamily: 'Outfit, sans-serif', fontWeight: 300, fontSize: '22px' }}>Clínica</span>
          </span>
        </a>
        <p className="nav-caption">WORKSPACE</p>
        <button
          className={`nav-link ${section === 'home' ? 'selected' : ''}`}
          onClick={() => setSection('home')}
          aria-current={section === 'home' ? 'page' : undefined}
        >
          <span><FontAwesomeIcon icon={faHouse} /></span> Início
        </button>
        <button
          className={`nav-link ${section === 'patients' ? 'selected' : ''}`}
          onClick={() => setSection('patients')}
          aria-current={section === 'patients' ? 'page' : undefined}
        >
          <span><FontAwesomeIcon icon={faNotesMedical} /></span> Pacientes
        </button>
        <button
          className={`nav-link ${section === 'appointments' ? 'selected' : ''}`}
          onClick={() => setSection('appointments')}
          aria-current={section === 'appointments' ? 'page' : undefined}
        >
          <span><FontAwesomeIcon icon={faCalendarDays} /></span> Agenda
        </button>
        <button
          className={`nav-link ${section === 'finance' ? 'selected' : ''}`}
          onClick={() => setSection('finance')}
          aria-current={section === 'finance' ? 'page' : undefined}
        >
          <span><FontAwesomeIcon icon={faWallet} /></span> Financeiro
        </button>
        <div className="sidebar-bottom">
          <div className="avatar">{user.name.slice(0, 1).toUpperCase()}</div>
          <div className="user-label">
            <strong>{user.name}</strong>
            <span>Workspace individual</span>
          </div>
          <button className="icon-button" onClick={logout} title="Sair" aria-label="Sair">
            <FontAwesomeIcon icon={faRightFromBracket} />
          </button>
        </div>
      </aside>

      <main className="main-content" id={section}>
        <header className="topbar">
          <span>
            Workspace <span className="crumb">/</span> {section === 'home' ? 'Início' : section === 'patients' ? 'Pacientes' : section === 'appointments' ? 'Agenda' : 'Financeiro'}
          </span>
          <span className="secure-label"><span className="secure-dot" /> Acesso privado</span>
        </header>
        <section className="page-content">
          {section === 'home' ? (
            <Home
              userName={user.name}
              onOpenPatients={() => setSection('patients')}
              onOpenAppointments={() => setSection('appointments')}
              onCreateAppointment={() => {
                setCreateAppointmentOnOpen(true)
                setSection('appointments')
              }}
            />
          ) : section === 'patients' ? <Patients /> : section === 'finance' ? <Finance /> : (
            <Appointments
              openCreateOnMount={createAppointmentOnOpen}
              onCreateRequestHandled={() => setCreateAppointmentOnOpen(false)}
            />
          )}
        </section>
      </main>
    </div>
  )
}
