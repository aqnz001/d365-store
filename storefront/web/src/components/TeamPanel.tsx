import { useEffect, useState } from 'react'
import { getMembers, addMember, updateMember, deleteMember, type CompanyMember, type CompanyRole } from '../api'
import { useCurrentUser } from '../context/auth'
import { Badge, Banner, Eyebrow, Loading, Spinner } from './ui'

const roles: CompanyRole[] = ['Buyer', 'Approver', 'Admin']
const roleTone: Record<CompanyRole, string> = { Admin: 'info', Approver: 'warn', Buyer: 'muted' }

const blank: CompanyMember = { userId: '', name: '', role: 'Buyer', spendLimit: null }

export function TeamPanel() {
  const user = useCurrentUser()
  const isAdmin = user?.role === 'Admin'
  const [members, setMembers] = useState<CompanyMember[]>()
  const [error, setError] = useState<string>()
  const [editing, setEditing] = useState<{ isNew: boolean; values: CompanyMember }>()
  const [busy, setBusy] = useState(false)

  const load = () => {
    getMembers()
      .then(setMembers)
      .catch((e: Error) => setError(e.message))
  }
  useEffect(load, [])

  const save = async () => {
    if (!editing) return
    setBusy(true)
    setError(undefined)
    try {
      if (editing.isNew) await addMember(editing.values)
      else await updateMember(editing.values.userId, editing.values)
      setEditing(undefined)
      load()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const remove = async (userId: string) => {
    if (!window.confirm(`Remove ${userId} from the company?`)) return
    setBusy(true)
    setError(undefined)
    try {
      await deleteMember(userId)
      load()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const set = (patch: Partial<CompanyMember>) =>
    setEditing((s) => (s ? { ...s, values: { ...s.values, ...patch } } : s))

  return (
    <section style={{ marginBottom: 28 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 12 }}>
        <div>
          <Eyebrow>Company</Eyebrow>
          <h2 style={{ fontSize: 20, margin: '6px 0 0' }}>Team &amp; roles</h2>
        </div>
        {isAdmin && !editing && (
          <button className="btn btn-sm" onClick={() => { setError(undefined); setEditing({ isNew: true, values: blank }) }}>
            Add member
          </button>
        )}
      </div>

      {!isAdmin && (
        <p className="muted" style={{ marginTop: 8, fontSize: 14 }}>
          You have the <strong>{user?.role ?? 'Buyer'}</strong> role. Only an admin can manage the team.
        </p>
      )}

      {error && <Banner kind="danger">{error}</Banner>}

      {editing && (
        <form className="panel panel-pad" style={{ marginTop: 16 }} onSubmit={(e) => { e.preventDefault(); void save() }}>
          <h3 style={{ fontSize: 16, marginBottom: 14 }}>{editing.isNew ? 'Add member' : 'Edit member'}</h3>
          <div className="addr-grid">
            <label className="addr-field">
              <span>Work email</span>
              <input
                type="email"
                value={editing.values.userId}
                onChange={(e) => set({ userId: e.target.value })}
                required
                maxLength={120}
                disabled={!editing.isNew}
                placeholder="name@company.com"
              />
            </label>
            <label className="addr-field">
              <span>Name</span>
              <input value={editing.values.name} onChange={(e) => set({ name: e.target.value })} required maxLength={80} />
            </label>
            <label className="addr-field">
              <span>Role</span>
              <select value={editing.values.role} onChange={(e) => set({ role: e.target.value as CompanyRole })}>
                {roles.map((r) => (
                  <option key={r} value={r}>{r}</option>
                ))}
              </select>
            </label>
            <label className="addr-field">
              <span>Spend limit <span className="muted">(optional)</span></span>
              <input
                type="number"
                min={0}
                step="0.01"
                value={editing.values.spendLimit ?? ''}
                onChange={(e) => set({ spendLimit: e.target.value === '' ? null : Number(e.target.value) })}
                placeholder="No limit"
              />
            </label>
          </div>
          <div style={{ display: 'flex', gap: 10, marginTop: 16 }}>
            <button className="btn btn-primary btn-sm" type="submit" disabled={busy}>
              {busy ? <Spinner /> : 'Save member'}
            </button>
            <button className="btn btn-sm" type="button" onClick={() => setEditing(undefined)} disabled={busy}>
              Cancel
            </button>
          </div>
        </form>
      )}

      {!members && !error && <Loading>Loading team…</Loading>}
      {members && members.length > 0 && (
        <div style={{ overflowX: 'auto', marginTop: 16 }}>
          <table className="ledger">
            <thead>
              <tr>
                <th>Member</th>
                <th>Role</th>
                <th style={{ textAlign: 'right' }}>Spend limit</th>
                {isAdmin && <th style={{ textAlign: 'right' }}>Manage</th>}
              </tr>
            </thead>
            <tbody>
              {members.map((m) => (
                <tr key={m.userId}>
                  <td>
                    <div style={{ fontWeight: 600 }}>{m.name}</div>
                    <div className="mono muted" style={{ fontSize: 12 }}>{m.userId}</div>
                  </td>
                  <td><Badge tone={roleTone[m.role]}>{m.role}</Badge></td>
                  <td className="tnum" style={{ textAlign: 'right' }}>
                    {m.spendLimit != null ? m.spendLimit.toLocaleString('en-GB', { style: 'currency', currency: 'GBP' }) : '—'}
                  </td>
                  {isAdmin && (
                    <td style={{ textAlign: 'right' }}>
                      <button className="btn-link" onClick={() => { setError(undefined); setEditing({ isNew: false, values: m }) }} disabled={busy}>Edit</button>
                      <button className="btn-link danger" onClick={() => void remove(m.userId)} disabled={busy} style={{ marginLeft: 10 }}>Remove</button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
