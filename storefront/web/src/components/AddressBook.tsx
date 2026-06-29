import { useEffect, useState } from 'react'
import { getAddresses, addAddress, updateAddress, deleteAddress, type Address, type AddressInput } from '../api'
import { Banner, Badge, Eyebrow, Loading, Spinner } from './ui'

const empty: AddressInput = {
  name: '',
  line1: '',
  line2: '',
  city: '',
  region: '',
  postalCode: '',
  country: '',
  isDefaultShipping: false,
  isDefaultBilling: false,
}

function toInput(a: Address): AddressInput {
  const { id: _id, ...rest } = a
  return rest
}

export function AddressBook() {
  const [addresses, setAddresses] = useState<Address[]>()
  const [error, setError] = useState<string>()
  const [editing, setEditing] = useState<{ id: string | null; values: AddressInput }>()
  const [busy, setBusy] = useState(false)

  const load = () => {
    getAddresses()
      .then(setAddresses)
      .catch((e: Error) => setError(e.message))
  }
  useEffect(load, [])

  const startAdd = () => {
    setError(undefined)
    setEditing({ id: null, values: empty })
  }
  const startEdit = (a: Address) => {
    setError(undefined)
    setEditing({ id: a.id, values: toInput(a) })
  }
  const cancel = () => setEditing(undefined)

  const save = async () => {
    if (!editing) return
    setBusy(true)
    setError(undefined)
    try {
      if (editing.id) await updateAddress(editing.id, editing.values)
      else await addAddress(editing.values)
      setEditing(undefined)
      load()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const remove = async (id: string) => {
    if (!window.confirm('Delete this address? This cannot be undone.')) return
    setBusy(true)
    try {
      await deleteAddress(id)
      load()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const set = (patch: Partial<AddressInput>) =>
    setEditing((s) => (s ? { ...s, values: { ...s.values, ...patch } } : s))

  return (
    <section style={{ marginBottom: 28 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 12 }}>
        <div>
          <Eyebrow>Address book</Eyebrow>
          <h2 style={{ fontSize: 20, margin: '6px 0 0' }}>Shipping &amp; billing addresses</h2>
        </div>
        {!editing && (
          <button className="btn btn-sm" onClick={startAdd}>
            Add address
          </button>
        )}
      </div>

      {error && <Banner kind="danger">{error}</Banner>}

      {editing && (
        <form
          className="panel panel-pad addr-form"
          style={{ marginTop: 16 }}
          onSubmit={(e) => {
            e.preventDefault()
            void save()
          }}
        >
          <h3 style={{ fontSize: 16, marginBottom: 14 }}>{editing.id ? 'Edit address' : 'New address'}</h3>
          <div className="addr-grid">
            <label className="addr-field span-2">
              <span>Contact / company name</span>
              <input value={editing.values.name} onChange={(e) => set({ name: e.target.value })} required maxLength={80} />
            </label>
            <label className="addr-field span-2">
              <span>Address line 1</span>
              <input value={editing.values.line1} onChange={(e) => set({ line1: e.target.value })} required maxLength={120} />
            </label>
            <label className="addr-field span-2">
              <span>Address line 2 <span className="muted">(optional)</span></span>
              <input value={editing.values.line2 ?? ''} onChange={(e) => set({ line2: e.target.value })} maxLength={120} />
            </label>
            <label className="addr-field">
              <span>City</span>
              <input value={editing.values.city} onChange={(e) => set({ city: e.target.value })} required maxLength={80} />
            </label>
            <label className="addr-field">
              <span>Region <span className="muted">(optional)</span></span>
              <input value={editing.values.region ?? ''} onChange={(e) => set({ region: e.target.value })} maxLength={80} />
            </label>
            <label className="addr-field">
              <span>Postal code</span>
              <input value={editing.values.postalCode} onChange={(e) => set({ postalCode: e.target.value })} required maxLength={16} />
            </label>
            <label className="addr-field">
              <span>Country</span>
              <input value={editing.values.country} onChange={(e) => set({ country: e.target.value })} required maxLength={56} placeholder="e.g. GB" />
            </label>
          </div>
          <div className="addr-checks">
            <label>
              <input
                type="checkbox"
                checked={editing.values.isDefaultShipping}
                onChange={(e) => set({ isDefaultShipping: e.target.checked })}
              />{' '}
              Default shipping
            </label>
            <label>
              <input
                type="checkbox"
                checked={editing.values.isDefaultBilling}
                onChange={(e) => set({ isDefaultBilling: e.target.checked })}
              />{' '}
              Default billing
            </label>
          </div>
          <div style={{ display: 'flex', gap: 10, marginTop: 16 }}>
            <button className="btn btn-primary btn-sm" type="submit" disabled={busy}>
              {busy ? <Spinner /> : 'Save address'}
            </button>
            <button className="btn btn-sm" type="button" onClick={cancel} disabled={busy}>
              Cancel
            </button>
          </div>
        </form>
      )}

      {!addresses && !error && <Loading>Loading addresses…</Loading>}
      {addresses && addresses.length === 0 && !editing && (
        <p className="muted" style={{ marginTop: 14 }}>No saved addresses yet.</p>
      )}
      {addresses && addresses.length > 0 && (
        <div className="addr-list" style={{ marginTop: 16 }}>
          {addresses.map((a) => (
            <div className="addr-card" key={a.id}>
              <div className="addr-badges">
                {a.isDefaultShipping && <Badge tone="info">Default shipping</Badge>}
                {a.isDefaultBilling && <Badge tone="muted">Default billing</Badge>}
              </div>
              <div className="addr-name">{a.name}</div>
              <div className="muted addr-lines">
                {a.line1}
                {a.line2 ? <>, {a.line2}</> : null}
                <br />
                {a.city}
                {a.region ? `, ${a.region}` : ''} {a.postalCode}
                <br />
                {a.country}
              </div>
              <div className="addr-actions">
                <button className="btn-link" onClick={() => startEdit(a)} disabled={busy}>Edit</button>
                <button className="btn-link danger" onClick={() => void remove(a.id)} disabled={busy}>Delete</button>
              </div>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}
