import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import {
    faHospitalUser,
    faCirclePlus,
    faMagnifyingGlass,
    faNotesMedical,
    faCircleInfo,
    faRightFromBracket
} from '@fortawesome/free-solid-svg-icons';

type User = { name: string; email: string }
type Patient = {
  id: string
  name: string
  contact: string | null
  isArchived: boolean
  createdAt: string
  updatedAt: string
}
type PatientForm = { name: string; contact: string }

async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'include',
    headers: {
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
      ...init.headers,
    },
  })
  if (!response.ok) {
    if (response.status === 401) throw new Error('Sessão encerrada. Entre novamente.')
    const body = await response.json().catch(() => null)
    const fieldErrors = body?.errors ? Object.values(body.errors).flat().join(' ') : ''
    throw new Error(fieldErrors || body?.title || 'Não foi possível concluir a operação.')
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

const emptyForm: PatientForm = { name: '', contact: '' }

export default function App() {
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState(true)
  const [patients, setPatients] = useState<Patient[]>([])
  const [includeArchived, setIncludeArchived] = useState(false)
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<Patient | null>(null)
  const [form, setForm] = useState<PatientForm>(emptyForm)
  const [showForm, setShowForm] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const loadPatients = useCallback(async () => {
    const query = new URLSearchParams({ includeArchived: String(includeArchived) })
    if (search.trim()) query.set('search', search.trim())
    setPatients(await api<Patient[]>(`/api/patients?${query}`))
  }, [includeArchived, search])

  useEffect(() => {
    api<User>('/api/auth/me')
      .then(setUser)
      .catch(() => setUser(null))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    if (!user) return
    loadPatients().catch((reason: Error) => setError(reason.message))
  }, [user, loadPatients])

  const activeCount = useMemo(() => patients.filter((patient) => !patient.isArchived).length, [patients])

  async function handleLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const values = new FormData(event.currentTarget)
    setError('')
    try {
      const signedIn = await api<User>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email: values.get('email'), password: values.get('password') }),
      })
      setUser(signedIn)
    } catch {
      setError('E-mail ou senha inválidos.')
    }
  }

  function startCreate() {
    setEditing(null)
    setForm(emptyForm)
    setError('')
    setShowForm(true)
  }

  function startEdit(patient: Patient) {
    setEditing(patient)
    setForm({ name: patient.name, contact: patient.contact ?? '' })
    setError('')
    setShowForm(true)
  }

  async function savePatient(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')
    try {
      const saved = await api<Patient>(editing ? `/api/patients/${editing.id}` : '/api/patients', {
        method: editing ? 'PUT' : 'POST',
        body: JSON.stringify(form),
      })
      setShowForm(false)
      setNotice(editing ? 'Cadastro atualizado.' : 'Paciente cadastrado.')
      setPatients((current) => editing
        ? current.map((patient) => patient.id === saved.id ? saved : patient)
        : [saved, ...current])
    } catch (reason) {
      setError((reason as Error).message)
    }
  }

  async function archivePatient(patient: Patient) {
    if (!window.confirm(`Arquivar o cadastro de ${patient.name}?`)) return
    setError('')
    try {
      await api<Patient>(`/api/patients/${patient.id}/archive`, { method: 'POST' })
      setNotice('Paciente arquivado. O cadastro foi mantido.')
      await loadPatients()
    } catch (reason) {
      setError((reason as Error).message)
    }
  }

  async function logout() {
    try {
      await api<void>('/api/auth/logout', { method: 'POST' })
    } finally {
      setUser(null)
      setPatients([])
    }
  }

  if (loading) return <main className="loading">Carregando workspace…</main>

  if (!user) {
    return (
      <main className="login-page">
        <section className="login-card">
          <div className="brand-mark">P</div>
          <p className="eyebrow">PSICOFLOW · WORKSPACE</p>
          <h1>Um espaço mais organizado para sua rotina.</h1>
          <p className="muted">Entre para acessar seus pacientes.</p>
          <form onSubmit={handleLogin} className="form-stack">
            <label>E-mail<input name="email" type="email" autoComplete="username" required /></label>
            <label>Senha<input name="password" type="password" autoComplete="current-password" required /></label>
            {error && <p className="error" role="alert">{error}</p>}
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
        <a className="brand" href="#"><span className="brand-mark small">P</span><span>psicoflow</span></a>
        <p className="nav-caption">WORKSPACE</p>
        <a className="nav-link selected" href="#patients"><span><FontAwesomeIcon icon={faNotesMedical} /></span> Pacientes</a>
        <div className="sidebar-bottom">
          <div className="avatar">{user.name.slice(0, 1).toUpperCase()}</div>
          <div className="user-label"><strong>{user.name}</strong><span>Workspace individual</span></div>
          <button className="icon-button" onClick={logout} title="Sair" aria-label="Sair"><FontAwesomeIcon icon={faRightFromBracket} /></button>
        </div>
      </aside>

      <main className="main-content" id="patients">
        <header className="topbar"><span>Workspace <span className="crumb">/</span> Pacientes</span><span className="secure-label"><span className="secure-dot" /> Acesso privado</span></header>
        <section className="page-content">
          <div className="page-heading">
            <div><p className="eyebrow">SEU CONSULTÓRIO</p><h1>Pacientes</h1><p className="muted">Cadastros organizados em um só lugar.</p></div>
                      {!includeArchived && <button className="button primary" onClick={startCreate}><span><FontAwesomeIcon icon={faCirclePlus} /></span> Novo paciente</button>}
          </div>

          <div className="summary-row">
            <div className="summary-card">
                <span className="summary-icon">
                    <FontAwesomeIcon icon={faHospitalUser} />
                </span>
                <div><strong>{activeCount}</strong>
                    <span>{includeArchived ? 'cadastros exibidos' : 'pacientes ativos'}</span>
                </div>
            </div>
            <div className="summary-note">Somente informações essenciais. Nenhuma anotação clínica é armazenada nesta etapa.</div>
          </div>

          {notice && <div className="notice" role="status">{notice}<button onClick={() => setNotice('')} aria-label="Fechar aviso">×</button></div>}
          {error && <div className="error-banner" role="alert">{error}<button onClick={() => setError('')} aria-label="Fechar erro">×</button></div>}

          <section className="directory-card">
            <div className="directory-header"><div><h2>Todos os pacientes</h2><p>Pesquise pelo nome ou contato.</p></div>
              <label className="archive-toggle"><input type="checkbox" checked={includeArchived} onChange={(event) => setIncludeArchived(event.target.checked)} /> Mostrar arquivados</label>
            </div>
            <div className="search-box"><span><FontAwesomeIcon icon={faMagnifyingGlass} /></span><input aria-label="Pesquisar pacientes" placeholder="Buscar paciente…" value={search} onChange={(event) => setSearch(event.target.value)} /></div>
            <div className="patient-list">
              {patients.length === 0 ? <div className="empty-state"><span className="empty-icon">?</span><strong>{search ? 'Nenhum resultado encontrado' : includeArchived ? 'Nenhum cadastro arquivado' : 'Sua lista começa aqui'}</strong><p>{search ? 'Tente buscar por outro nome.' : 'Adicione o primeiro paciente para organizar seu workspace.'}</p>{!search && !includeArchived && <button className="button secondary" onClick={startCreate}>Cadastrar paciente</button>}</div> : patients.map((patient) => (
                <article className={`patient-row ${patient.isArchived ? 'archived' : ''}`} key={patient.id}>
                  <div className="patient-avatar">{patient.name.trim().slice(0, 1).toUpperCase()}</div>
                  <div className="patient-info"><strong>{patient.name}</strong><span>{patient.contact || 'Sem contato informado'}</span></div>
                  {patient.isArchived && <span className="status-pill">Arquivado</span>}
                  <time>{new Intl.DateTimeFormat('pt-BR').format(new Date(patient.updatedAt))}</time>
                  {!patient.isArchived && <div className="row-actions"><button className="text-button" onClick={() => startEdit(patient)}>Editar</button><button className="text-button archive-action" onClick={() => archivePatient(patient)}>Arquivar</button></div>}
                </article>
              ))}
            </div>
            <footer className="list-footer">{patients.length} {patients.length === 1 ? 'cadastro' : 'cadastros'} {includeArchived ? 'na visualização' : 'ativos'}</footer>
          </section>
          <p className="privacy-note"><span><FontAwesomeIcon icon={faCircleInfo} /></span> Dados desta demonstração são sintéticos. Não use informações reais de pacientes.</p>
        </section>
      </main>

      {showForm && <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setShowForm(false) }}>
        <section className="modal" role="dialog" aria-modal="true" aria-labelledby="form-title">
          <button className="modal-close" onClick={() => setShowForm(false)} aria-label="Fechar">×</button>
          <p className="eyebrow">CADASTRO ESSENCIAL</p><h2 id="form-title">{editing ? 'Editar paciente' : 'Novo paciente'}</h2><p className="muted">Inclua apenas os dados necessários para identificar o cadastro.</p>
          <form className="form-stack" onSubmit={savePatient}>
            <label>Nome completo <span className="required">*</span><input autoFocus maxLength={150} value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} required /></label>
            <label>Contato <span className="optional">(opcional)</span><input maxLength={200} value={form.contact} onChange={(event) => setForm({ ...form, contact: event.target.value })} placeholder="Telefone ou e-mail" /></label>
            {error && <p className="error" role="alert">{error}</p>}
            <div className="modal-actions"><button type="button" className="button secondary" onClick={() => setShowForm(false)}>Cancelar</button><button className="button primary" type="submit">{editing ? 'Salvar alterações' : 'Cadastrar paciente'}</button></div>
          </form>
        </section>
      </div>}
    </div>
  )
}
