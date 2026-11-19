import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faAddressBook,
  faCircleInfo,
  faCirclePlus,
  faHospitalUser,
  faMagnifyingGlass,
} from '@fortawesome/free-solid-svg-icons'
import { api } from '../../api'
import NumericText from '../../NumericText'
import PatientDetails from '../PatientDetails/PatientDetails'
import './Patients.css'

type Patient = {
  id: string
  name: string
  contact: string | null
  phone: string | null
  email: string | null
  birthDate: string | null
  profession: string | null
  frequency: string
  patientStatus: string
  usualDay: string | null
  usualTime: string | null
  fee: number
  chiefComplaint: string | null
  observations: string | null
  isArchived: boolean
  createdAt: string
  updatedAt: string
}

type PatientForm = {
  name: string; contact: string; phone: string; email: string; birthDate: string; profession: string
  frequency: string; patientStatus: string; usualDay: string; usualTime: string; fee: string
  chiefComplaint: string; observations: string
}

const emptyForm: PatientForm = {
  name: '', contact: '', phone: '', email: '', birthDate: '', profession: '', frequency: 'weekly',
  patientStatus: 'active', usualDay: '', usualTime: '', fee: '0', chiefComplaint: '', observations: '',
}

export default function Patients() {
  const [patients, setPatients] = useState<Patient[]>([])
  const [selectedPatientId, setSelectedPatientId] = useState<string | null>(null)
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
    loadPatients().catch((reason: Error) => setError(reason.message))
  }, [loadPatients])

  const activeCount = useMemo(
    () => patients.filter((patient) => !patient.isArchived).length,
    [patients],
  )

  if (selectedPatientId) {
    return (
      <PatientDetails
        patientId={selectedPatientId}
        onBack={() => setSelectedPatientId(null)}
      />
    )
  }

  function startCreate() {
    setEditing(null)
    setForm(emptyForm)
    setError('')
    setShowForm(true)
  }

  function startEdit(patient: Patient) {
    setEditing(patient)
    setForm({
      name: patient.name, contact: patient.contact ?? '', phone: patient.phone ?? '', email: patient.email ?? '',
      birthDate: patient.birthDate ?? '', profession: patient.profession ?? '', frequency: patient.frequency,
      patientStatus: patient.patientStatus, usualDay: patient.usualDay ?? '', usualTime: patient.usualTime?.slice(0, 5) ?? '',
      fee: String(patient.fee), chiefComplaint: patient.chiefComplaint ?? '', observations: patient.observations ?? '',
    })
    setError('')
    setShowForm(true)
  }

  async function savePatient(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')

    try {
      const saved = await api<Patient>(editing ? `/api/patients/${editing.id}` : '/api/patients', {
        method: editing ? 'PUT' : 'POST',
        body: JSON.stringify({
          ...form, birthDate: form.birthDate || null, usualTime: form.usualTime || null,
          fee: Number(form.fee),
        }),
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

  return (
    <>
      <div className="page-heading">
        <div>
          <p className="eyebrow">SEU CONSULTÓRIO</p>
          <h1>Pacientes</h1>
        </div>
        {!includeArchived && (
          <button className="button primary" onClick={startCreate}>
            <span><FontAwesomeIcon icon={faCirclePlus} /></span> Novo paciente
          </button>
        )}
      </div>

      <div className="summary-row">
        <div className="summary-card">
          <span className="summary-icon"><FontAwesomeIcon icon={faHospitalUser} /></span>
          <div>
            <strong><NumericText value={activeCount} /></strong>
            <span>{includeArchived ? 'cadastros exibidos' : 'pacientes ativos'}</span>
          </div>
        </div>
        <div className="summary-note">
          Acesse os registros e pendências ao selecionar um paciente.
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

      <section className="directory-card">
        <div className="directory-header">
          <div>
            <h2>Todos os pacientes</h2>
            <p>Pesquise pelo nome ou contato.</p>
          </div>
          <label className="archive-toggle">
            <input
              type="checkbox"
              checked={includeArchived}
              onChange={(event) => setIncludeArchived(event.target.checked)}
            />
            {' '}Mostrar arquivados
          </label>
        </div>
        <div className="search-box">
          <span><FontAwesomeIcon icon={faMagnifyingGlass} /></span>
          <input
            aria-label="Pesquisar pacientes"
            placeholder="Buscar paciente…"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
        </div>
        <div className="patient-list">
          {patients.length === 0 ? (
            <div className="empty-state">
              <span className="empty-icon"><FontAwesomeIcon icon={faAddressBook} /></span>
              <strong>
                {search
                  ? 'Nenhum resultado encontrado'
                  : includeArchived
                    ? 'Nenhum cadastro arquivado'
                    : 'Sua lista começa aqui'}
              </strong>
              <p>
                {search
                  ? 'Tente buscar por outro nome.'
                  : 'Adicione o primeiro paciente para organizar seu workspace.'}
              </p>
              {!search && !includeArchived && (
                <button className="button secondary" onClick={startCreate}>Cadastrar paciente</button>
              )}
            </div>
          ) : patients.map((patient) => (
            <article
              className={`patient-row ${patient.isArchived ? 'archived' : ''}`}
              key={patient.id}
            >
              <div className="patient-avatar">{patient.name.trim().slice(0, 1).toUpperCase()}</div>
              <div className="patient-info">
                <button
                  className="patient-name-link"
                  onClick={() => setSelectedPatientId(patient.id)}
                >
                  {patient.name}
                </button>
                <span>{patient.phone || patient.email || patient.contact || 'Sem contato informado'}</span>
              </div>
              {patient.isArchived && <span className="status-pill">Arquivado</span>}
              <time><NumericText value={new Intl.DateTimeFormat('pt-BR').format(new Date(patient.updatedAt))} /></time>
              {!patient.isArchived && (
                <div className="row-actions">
                  <button className="text-button" onClick={() => startEdit(patient)}>Editar</button>
                  <button
                    className="text-button archive-action"
                    onClick={() => archivePatient(patient)}
                  >
                    Arquivar
                  </button>
                </div>
              )}
            </article>
          ))}
        </div>
        <footer className="list-footer">
          {patients.length} {patients.length === 1 ? 'cadastro' : 'cadastros'}{' '}
          {includeArchived ? 'na visualização' : 'ativos'}
        </footer>
      </section>
      <p className="privacy-note">
        <span><FontAwesomeIcon icon={faCircleInfo} /></span>
        {' '}Dados desta demonstração são sintéticos. Não use informações reais de pacientes.
      </p>

      {showForm && (
        <div
          className="modal-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) setShowForm(false)
          }}
        >
          <section className="modal patient-modal" role="dialog" aria-modal="true" aria-labelledby="form-title">
            <button className="modal-close" onClick={() => setShowForm(false)} aria-label="Fechar">
              ×
            </button>
            <p className="eyebrow">CADASTRO DO PACIENTE</p>
            <h2 id="form-title">{editing ? 'Editar paciente' : 'Novo paciente'}</h2>
            <p className="muted">Preencha os dados necessários para o cadastro.</p>
            <form className="form-stack" onSubmit={savePatient}>
              <label>
                Nome completo <span className="required">*</span>
                <input autoFocus maxLength={150} value={form.name} placeholder="Nome do paciente"
                  onChange={(event) => setForm({ ...form, name: event.target.value })} required />
              </label>
              {editing?.contact && (
                <label>Contato anterior
                  <input maxLength={200} value={form.contact}
                    onChange={(event) => setForm({ ...form, contact: event.target.value })} />
                </label>
              )}
              <div className="patient-form-grid">
                <label>Telefone
                  <input type="tel" maxLength={200} placeholder="(11) 90000-0000" value={form.phone}
                    onChange={(event) => setForm({ ...form, phone: event.target.value })} />
                </label>
                <label>E-mail
                  <input type="email" maxLength={200} placeholder="email@exemplo.com" value={form.email}
                    onChange={(event) => setForm({ ...form, email: event.target.value })} />
                </label>
                <label>Nascimento
                  <input type="date" value={form.birthDate}
                    onChange={(event) => setForm({ ...form, birthDate: event.target.value })} />
                </label>
                <label>Profissão
                  <input maxLength={150} placeholder="Opcional" value={form.profession}
                    onChange={(event) => setForm({ ...form, profession: event.target.value })} />
                </label>
                <label>Frequência
                  <select value={form.frequency} onChange={(event) => setForm({ ...form, frequency: event.target.value })}>
                    <option value="weekly">Semanal</option><option value="biweekly">Quinzenal</option>
                    <option value="monthly">Mensal</option><option value="other">Outra</option>
                  </select>
                </label>
                <label>Situação
                  <select value={form.patientStatus} onChange={(event) => setForm({ ...form, patientStatus: event.target.value })}>
                    <option value="active">Ativo</option><option value="inactive">Inativo</option>
                  </select>
                </label>
                <label>Dia habitual
                  <select value={form.usualDay} onChange={(event) => setForm({ ...form, usualDay: event.target.value })}>
                    <option value="">Selecione</option><option value="monday">Segunda-feira</option>
                    <option value="tuesday">Terça-feira</option><option value="wednesday">Quarta-feira</option>
                    <option value="thursday">Quinta-feira</option><option value="friday">Sexta-feira</option>
                    <option value="saturday">Sábado</option><option value="sunday">Domingo</option>
                  </select>
                </label>
                <div className="patient-form-time-fee">
                  <label>Horário
                    <input type="time" value={form.usualTime} onChange={(event) => setForm({ ...form, usualTime: event.target.value })} />
                  </label>
                  <label>Valor (R$)
                    <input type="number" min="0" max="10000000" step="0.01" required value={form.fee}
                      onChange={(event) => setForm({ ...form, fee: event.target.value })} />
                  </label>
                </div>
              </div>
              <label>Queixa principal
                <textarea maxLength={5000} placeholder="Demanda inicial trazida pelo paciente" value={form.chiefComplaint}
                  onChange={(event) => setForm({ ...form, chiefComplaint: event.target.value })} />
              </label>
              <label>Observações
                <textarea maxLength={5000} value={form.observations}
                  onChange={(event) => setForm({ ...form, observations: event.target.value })} />
              </label>
              {error && <p className="error" role="alert">{error}</p>}
              <div className="modal-actions">
                <button
                  type="button"
                  className="button secondary"
                  onClick={() => setShowForm(false)}
                >
                  Cancelar
                </button>
                <button className="button primary" type="submit">
                  {editing ? 'Salvar alterações' : 'Cadastrar paciente'}
                </button>
              </div>
            </form>
          </section>
        </div>
      )}
    </>
  )
}
