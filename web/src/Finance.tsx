import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowDown, faArrowUp, faChevronLeft, faChevronRight, faPen, faPlus, faTrashCan, faWallet } from '@fortawesome/free-solid-svg-icons'
import { api } from './api'
import NumericText from './NumericText'

type EntryType = 'revenue' | 'expense'
type EntryStatus = 'paid' | 'pending'
type FinancialEntry = {
  id: string
  patientId: string | null
  patientName: string | null
  description: string
  type: EntryType
  status: EntryStatus
  amount: number
  entryDate: string
  createdAt: string
  updatedAt: string
}
type Patient = { id: string; name: string; isArchived: boolean }
type EntryForm = {
  patientId: string
  description: string
  type: EntryType
  status: EntryStatus
  amount: string
  entryDate: string
}

function dateValue(date: Date) {
  const pad = (part: number) => String(part).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
}

function monthLabel(date: Date) {
  const label = new Intl.DateTimeFormat('pt-BR', { month: 'long', year: 'numeric' }).format(date)
  return label.charAt(0).toUpperCase() + label.slice(1)
}

function formatEntryDate(value: string) {
  const [year, month, day] = value.split('-').map(Number)
  return new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: 'short' }).format(new Date(year, month - 1, day))
}

function newEntryForm(): EntryForm {
  return {
    patientId: '',
    description: '',
    type: 'revenue',
    status: 'paid',
    amount: '',
    entryDate: dateValue(new Date()),
  }
}

export default function Finance() {
  const [month, setMonth] = useState(() => {
    const date = new Date()
    return new Date(date.getFullYear(), date.getMonth(), 1)
  })
  const [entries, setEntries] = useState<FinancialEntry[]>([])
  const [patients, setPatients] = useState<Patient[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [showForm, setShowForm] = useState(false)
  const [editingEntry, setEditingEntry] = useState<FinancialEntry | null>(null)
  const [form, setForm] = useState<EntryForm>(newEntryForm)
  const [saving, setSaving] = useState(false)

  const loadEntries = useCallback(async () => {
    const query = new URLSearchParams({ year: String(month.getFullYear()), month: String(month.getMonth() + 1) })
    setEntries(await api<FinancialEntry[]>(`/api/financial-entries?${query}`))
  }, [month])

  useEffect(() => {
    setLoading(true)
    setError('')
    Promise.all([
      loadEntries(),
      api<Patient[]>('/api/patients?includeArchived=false').then(setPatients),
    ]).catch((reason: unknown) => {
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os dados financeiros.')
    }).finally(() => setLoading(false))
  }, [loadEntries])

  const totals = useMemo(() => {
    const received = entries
      .filter((entry) => entry.type === 'revenue' && entry.status === 'paid')
      .reduce((sum, entry) => sum + entry.amount, 0)
    const receivable = entries
      .filter((entry) => entry.type === 'revenue' && entry.status === 'pending')
      .reduce((sum, entry) => sum + entry.amount, 0)
    const expenses = entries
      .filter((entry) => entry.type === 'expense')
      .reduce((sum, entry) => sum + entry.amount, 0)
    return { received, receivable, expenses, balance: received - expenses }
  }, [entries])

  function startCreate() {
    setEditingEntry(null)
    setForm(newEntryForm())
    setError('')
    setShowForm(true)
  }

  function startEdit(entry: FinancialEntry) {
    setEditingEntry(entry)
    setForm({
      patientId: entry.patientId ?? '',
      description: entry.description,
      type: entry.type,
      status: entry.status,
      amount: String(entry.amount),
      entryDate: entry.entryDate,
    })
    setError('')
    setShowForm(true)
  }

  async function saveEntry(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')
    setSaving(true)
    try {
      const payload = {
        patientId: form.patientId || null,
        description: form.description.trim(),
        type: form.type,
        status: form.status,
        amount: Number(form.amount),
        entryDate: form.entryDate,
      }
      await api<FinancialEntry>(
        editingEntry ? `/api/financial-entries/${editingEntry.id}` : '/api/financial-entries',
        { method: editingEntry ? 'PUT' : 'POST', body: JSON.stringify(payload) },
      )
      setShowForm(false)
      setNotice(editingEntry ? 'Lançamento atualizado.' : 'Lançamento criado.')
      await loadEntries()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar o lançamento.')
    } finally {
      setSaving(false)
    }
  }

  async function deleteEntry(entry: FinancialEntry) {
    if (!window.confirm(`Excluir o lançamento “${entry.description}”?`)) return
    setError('')
    try {
      await api<void>(`/api/financial-entries/${entry.id}`, { method: 'DELETE' })
      setNotice('Lançamento excluído.')
      await loadEntries()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível excluir o lançamento.')
    }
  }

  return (
    <>
      <div className="page-heading">
        <div>
          <p className="eyebrow">SEU CONSULTÓRIO</p>
          <h1>Financeiro</h1>
        </div>
        <div className="finance-heading-actions">
          <div className="finance-month-picker" aria-label="Selecionar mês">
            <button type="button" aria-label="Mês anterior" onClick={() => setMonth((current) => new Date(current.getFullYear(), current.getMonth() - 1, 1))}>
              <FontAwesomeIcon icon={faChevronLeft} />
            </button>
            <span><NumericText value={monthLabel(month)} /></span>
            <button type="button" aria-label="Próximo mês" onClick={() => setMonth((current) => new Date(current.getFullYear(), current.getMonth() + 1, 1))}>
              <FontAwesomeIcon icon={faChevronRight} />
            </button>
          </div>
          <button className="button primary finance-create-button" onClick={startCreate}>
            <FontAwesomeIcon icon={faPlus} /> Novo lançamento
          </button>
        </div>
      </div>

      {notice && <div className="notice" role="status">{notice}<button onClick={() => setNotice('')} aria-label="Fechar aviso">×</button></div>}
      {error && !showForm && <div className="error-banner" role="alert">{error}<button onClick={() => setError('')} aria-label="Fechar erro">×</button></div>}

      <section className="finance-summary" aria-label="Resumo financeiro do mês">
        <article className="finance-card home-card">
          <div className="finance-card-label home-card-heading"><span>RECEBIDO</span><i className="home-card-icon received"><FontAwesomeIcon icon={faArrowDown} /></i></div>
          <strong><NumericText value={formatCurrency(totals.received)} /></strong>
          <span className="home-card-caption">Sessões e serviços pagos</span>
        </article>
        <article className="finance-card home-card">
          <div className="finance-card-label home-card-heading"><span>A RECEBER</span><i className="home-card-icon pending"><FontAwesomeIcon icon={faWallet} /></i></div>
          <strong><NumericText value={formatCurrency(totals.receivable)} /></strong>
          <span className="home-card-caption">Pagamentos pendentes</span>
        </article>
        <article className="finance-card home-card">
          <div className="finance-card-label home-card-heading"><span>DESPESAS</span><i className="home-card-icon expenses"><FontAwesomeIcon icon={faArrowUp} /></i></div>
          <strong><NumericText value={formatCurrency(totals.expenses)} /></strong>
          <span className="home-card-caption">Custos do consultório</span>
        </article>
        <article className="finance-card home-card">
          <div className="finance-card-label home-card-heading"><span>SALDO DO MÊS</span><i className="home-card-icon balance"><FontAwesomeIcon icon={faWallet} /></i></div>
          <strong><NumericText value={formatCurrency(totals.balance)} /></strong>
          <span className="home-card-caption">Recebido menos despesas</span>
        </article>
      </section>

      <section className="finance-ledger directory-card" aria-labelledby="finance-ledger-title">
        <div className="finance-ledger-heading">
          <h2 id="finance-ledger-title">Lançamentos</h2>
          <span><NumericText value={entries.length} /> {entries.length === 1 ? 'lançamento no mês' : 'lançamentos no mês'}</span>
        </div>
        {loading ? (
          <div className="finance-empty"><p className="muted" role="status">Carregando lançamentos…</p></div>
        ) : entries.length === 0 ? (
          <div className="finance-empty">
            <span className="finance-empty-icon"><FontAwesomeIcon icon={faWallet} /></span>
            <strong>Nenhum lançamento neste mês</strong>
            <p>Registre as sessões pagas e as despesas para<br className="desktop-break" /> acompanhar o resultado do consultório.</p>
            <button className="button primary finance-create-button" onClick={startCreate}><FontAwesomeIcon icon={faPlus} /> Novo lançamento</button>
          </div>
        ) : (
          <div className="finance-entry-list">
            {entries.map((entry) => (
              <article className="finance-entry" key={entry.id}>
                <time dateTime={entry.entryDate}><NumericText value={formatEntryDate(entry.entryDate)} /></time>
                <span className={`finance-entry-icon ${entry.type}`}><FontAwesomeIcon icon={entry.type === 'revenue' ? faArrowDown : faArrowUp} /></span>
                <div className="finance-entry-description">
                  <strong>{entry.description}</strong>
                  <span>{entry.patientName ?? (entry.type === 'revenue' ? 'Receita' : 'Despesa')}</span>
                </div>
                <span className={`finance-entry-status ${entry.status}`}>{entry.status === 'paid' ? (entry.type === 'revenue' ? 'Recebido' : 'Pago') : 'Pendente'}</span>
                <strong className={`finance-entry-amount ${entry.type}`}><NumericText value={formatCurrency(entry.amount)} /></strong>
                <div className="finance-entry-actions">
                  <button type="button" onClick={() => startEdit(entry)} aria-label={`Editar ${entry.description}`} title="Editar"><FontAwesomeIcon icon={faPen} /></button>
                  <button type="button" onClick={() => deleteEntry(entry)} aria-label={`Excluir ${entry.description}`} title="Excluir"><FontAwesomeIcon icon={faTrashCan} /></button>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>

      {showForm && (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
          if (event.target === event.currentTarget && !saving) setShowForm(false)
        }}>
          <section className="modal finance-modal" role="dialog" aria-modal="true" aria-labelledby="finance-form-title">
            <button className="modal-close" onClick={() => setShowForm(false)} aria-label="Fechar">×</button>
            <p className="eyebrow">FINANCEIRO</p>
            <h2 id="finance-form-title">{editingEntry ? 'Editar lançamento' : 'Novo lançamento'}</h2>
            <p className="muted">Informe os dados para acompanhar suas movimentações.</p>
            <form className="form-stack" onSubmit={saveEntry}>
              <label>
                Tipo de lançamento <span className="required">*</span>
                <select value={form.type} onChange={(event) => setForm({ ...form, type: event.target.value === 'expense' ? 'expense' : 'revenue' })}>
                  <option value="revenue">Receita</option>
                  <option value="expense">Despesa</option>
                </select>
              </label>
              <label>
                Descrição <span className="required">*</span>
                <input type="text" maxLength={200} value={form.description} onChange={(event) => setForm({ ...form, description: event.target.value })} required autoFocus />
              </label>
              <label>
                Paciente <span className="optional">(opcional)</span>
                <select value={form.patientId} onChange={(event) => setForm({ ...form, patientId: event.target.value })}>
                  <option value="">Sem paciente vinculado</option>
                  {patients.map((patient) => <option key={patient.id} value={patient.id}>{patient.name}</option>)}
                </select>
              </label>
              <div className="finance-form-row">
                <label>
                  Valor <span className="required">*</span>
                  <input type="number" min="0.01" max="999999999.99" step="0.01" value={form.amount} onChange={(event) => setForm({ ...form, amount: event.target.value })} required />
                </label>
                <label>
                  Data <span className="required">*</span>
                  <input type="date" value={form.entryDate} onChange={(event) => setForm({ ...form, entryDate: event.target.value })} required />
                </label>
              </div>
              <label>
                {form.type === 'revenue' ? 'Status do recebimento' : 'Status do pagamento'}
                <select value={form.status} onChange={(event) => setForm({ ...form, status: event.target.value === 'pending' ? 'pending' : 'paid' })}>
                  <option value="paid">{form.type === 'revenue' ? 'Recebido' : 'Pago'}</option>
                  <option value="pending">Pendente</option>
                </select>
              </label>
              {error && <p className="error" role="alert">{error}</p>}
              <div className="modal-actions">
                <button type="button" className="button secondary" onClick={() => setShowForm(false)} disabled={saving}>Cancelar</button>
                <button className="button primary" type="submit" disabled={saving}>{saving ? 'Salvando…' : editingEntry ? 'Salvar alterações' : 'Salvar lançamento'}</button>
              </div>
            </form>
          </section>
        </div>
      )}
    </>
  )
}
