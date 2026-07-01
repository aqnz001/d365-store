import { useEffect, useState } from 'react'
import { getApprovals, approveOrder, rejectOrder, type PendingApproval } from '../api'
import { useCurrentUser } from '../context/auth'
import { formatMoney } from '../format'
import { Badge, Banner, Eyebrow, EmptyState, Loading, Spinner } from './ui'

const statusTone: Record<PendingApproval['status'], string> = { Pending: 'warn', Approved: 'ok', Rejected: 'danger' }

export function ApprovalsPanel() {
  const user = useCurrentUser()
  const canApprove = user?.role === 'Approver' || user?.role === 'Admin'
  const [approvals, setApprovals] = useState<PendingApproval[]>()
  const [error, setError] = useState<string>()
  const [busyId, setBusyId] = useState<string>()

  const load = () => {
    getApprovals()
      .then(setApprovals)
      .catch((e: Error) => setError(e.message))
  }
  useEffect(load, [])

  const act = async (id: string, action: (id: string) => Promise<unknown>) => {
    setBusyId(id)
    setError(undefined)
    try {
      await action(id)
      load()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusyId(undefined)
    }
  }

  // Buyers with nothing pending don't need an empty panel; approvers always see the queue.
  if (!approvals && !error) return <Loading>Loading approvals…</Loading>
  if (approvals && approvals.length === 0 && !canApprove) return null

  const pending = (approvals ?? []).filter((a) => a.status === 'Pending')

  return (
    <section style={{ marginBottom: 28 }}>
      <Eyebrow>Company</Eyebrow>
      <h2 style={{ fontSize: 20, margin: '6px 0 16px' }}>
        {canApprove ? 'Orders awaiting approval' : 'Your orders awaiting approval'}
      </h2>

      {error && <Banner kind="danger">{error}</Banner>}

      {canApprove && pending.length === 0 && (
        <EmptyState title="Nothing to approve">No orders are waiting for approval right now.</EmptyState>
      )}

      {approvals && approvals.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table className="ledger">
            <thead>
              <tr>
                <th>Placed by</th>
                <th>Placed</th>
                <th style={{ textAlign: 'right' }}>Order value</th>
                <th style={{ textAlign: 'right' }}>Status</th>
                {canApprove && <th style={{ textAlign: 'right' }}>Decision</th>}
              </tr>
            </thead>
            <tbody>
              {approvals.map((a) => (
                <tr key={a.id}>
                  <td>
                    <div style={{ fontWeight: 600 }}>{a.buyerName}</div>
                    <div className="mono muted" style={{ fontSize: 12 }}>
                      {a.lines.length} line{a.lines.length === 1 ? '' : 's'}
                      {a.poNumber ? ` · PO ${a.poNumber}` : ''}
                    </div>
                  </td>
                  <td className="muted tnum">{new Date(a.placedAtUtc).toLocaleString()}</td>
                  <td className="tnum" style={{ textAlign: 'right' }}>{formatMoney(a.amount)}</td>
                  <td style={{ textAlign: 'right' }}>
                    <Badge tone={statusTone[a.status]}>{a.status}</Badge>
                    {a.orderReference && (
                      <div className="mono muted" style={{ fontSize: 12, marginTop: 2 }}>{a.orderReference}</div>
                    )}
                  </td>
                  {canApprove && (
                    <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                      {a.status === 'Pending' ? (
                        busyId === a.id ? (
                          <Spinner />
                        ) : (
                          <>
                            <button className="btn-link" onClick={() => void act(a.id, approveOrder)}>Approve</button>
                            <button className="btn-link danger" onClick={() => void act(a.id, rejectOrder)} style={{ marginLeft: 10 }}>
                              Reject
                            </button>
                          </>
                        )
                      ) : (
                        <span className="muted">—</span>
                      )}
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
