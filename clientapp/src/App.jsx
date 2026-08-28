import React, { useEffect, useState } from 'react'
import { Routes, Route, NavLink, Outlet } from 'react-router-dom'
import { api, fmtMoney, fmtDate, TYPES, STATUS, CLAIMS } from './api'

function Badge({ text, css }) { return <span className={`badge ${css || 'secondary'}`}>{text}</span> }
function Flash({ msg }) { return msg ? <div className={`flash ${msg.ok ? 'ok' : 'err'}`}>{msg.text}</div> : null }
function Modal({ title, onClose, wide, children }) {
  return (
    <div className="modal-bg" onClick={onClose}>
      <div className="modal" style={wide ? { maxWidth: 720 } : undefined} onClick={e => e.stopPropagation()}>
        <div className="row" style={{ marginBottom: 12 }}><h2 style={{ flex: 1, margin: 0 }}>{title}</h2>
          <button className="btn gray sm" style={{ flex: 'none' }} onClick={onClose}>Đóng</button></div>{children}
      </div>
    </div>
  )
}
function Field({ label, children }) { return <div style={{ flex: 1 }}><label>{label}</label>{children}</div> }

function Layout() {
  return (
    <>
      <nav className="nav"><span className="brand">🛡️ MiniInsurance</span>
        <NavLink to="/" end>Tổng quan</NavLink><NavLink to="/policies">Hợp đồng BH</NavLink><NavLink to="/insurers">Công ty BH</NavLink></nav>
      <div className="wrap"><Outlet /></div>
    </>
  )
}

function Dashboard() {
  const [d, setD] = useState(null); const [cache, setCache] = useState('')
  useEffect(() => { api.dashboard().then(r => { setD(r.data); setCache(r.cache) }) }, [])
  if (!d) return <p className="muted">Đang tải…</p>
  const max = Math.max(1, ...d.byType.map(t => t.count))
  return (
    <>
      <h1>Tổng quan bảo hiểm {cache && <span className="pill">cache: {cache}</span>}</h1>
      <div className="grid kpis" style={{ marginBottom: 18 }}>
        <div className="kpi"><div className="v">{d.policies}</div><div className="l">Tổng HĐ</div></div>
        <div className="kpi"><div className="v" style={{ color: 'var(--success)' }}>{d.active}</div><div className="l">Đang hiệu lực</div></div>
        <div className="kpi"><div className="v" style={{ color: 'var(--warning)' }}>{d.expiringSoon}</div><div className="l">Sắp hết hạn (30N)</div></div>
        <div className="kpi"><div className="v">{d.openClaims}</div><div className="l">Bồi thường mở</div></div>
        <div className="kpi"><div className="v" style={{ fontSize: 18, color: 'var(--success)' }}>{fmtMoney(d.premiumMonth)}</div><div className="l">Phí thu tháng</div></div>
      </div>
      <div className="card funnel"><h2>HĐ theo loại BH</h2>
        {d.byType.map((t, i) => (<div className="bar" key={i}><div className="lbl">{t.type}</div>
          <div className="track"><div className="fill" style={{ width: `${(t.count / max) * 100}%` }} /></div><div className="n">{t.count}</div></div>))}
      </div>
    </>
  )
}

function Policies() {
  const [rows, setRows] = useState([]); const [status, setStatus] = useState(''); const [q, setQ] = useState('')
  const [open, setOpen] = useState(null); const [creating, setCreating] = useState(false)
  const load = () => api.policies(status === '' ? null : Number(status), q).then(r => setRows(r.data))
  useEffect(() => { load() }, [status])
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 'none' }}>Hợp đồng bảo hiểm</h1><div className="sp" />
        <select style={{ maxWidth: 150 }} value={status} onChange={e => setStatus(e.target.value)}><option value="">— Trạng thái —</option>{STATUS.map((s, i) => <option key={i} value={i}>{s}</option>)}</select>
        <input style={{ maxWidth: 180 }} placeholder="Tìm KH/biển số…" value={q} onChange={e => setQ(e.target.value)} onKeyDown={e => e.key === 'Enter' && load()} />
        <button className="btn ghost sm" style={{ flex: 'none' }} onClick={load}>Tìm</button>
        <button className="btn sm" style={{ flex: 'none' }} onClick={() => setCreating(true)}>+ Tạo HĐ</button></div>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Mã</th><th>Khách</th><th>Biển số</th><th>Loại</th><th className="right">Phí</th><th>Hết hạn</th><th>Trạng thái</th></tr></thead>
          <tbody>{rows.map(p => (
            <tr key={p.id} style={{ cursor: 'pointer' }} onClick={() => setOpen(p.id)}>
              <td>{p.code}</td><td>{p.customerName}</td><td>{p.vehiclePlate}</td><td>{p.type}</td>
              <td className="right">{fmtMoney(p.premium)}</td><td>{fmtDate(p.endDate)}{p.expiringSoon && <span className="badge warning" style={{ marginLeft: 4 }}>{p.daysToExpiry}N</span>}</td>
              <td><Badge text={p.statusText} css={p.statusCss} /></td></tr>))}
            {rows.length === 0 && <tr><td colSpan={7} className="muted" style={{ padding: 20 }}>Không có hợp đồng.</td></tr>}</tbody></table>
      </div>
      {open && <PolicyDetail id={open} onClose={() => setOpen(null)} onChanged={load} />}
      {creating && <PolicyForm onClose={() => setCreating(false)} onSaved={() => { setCreating(false); load() }} />}
    </>
  )
}

function PolicyDetail({ id, onClose, onChanged }) {
  const [p, setP] = useState(null); const [msg, setMsg] = useState(null)
  const [pay, setPay] = useState(''); const [claim, setClaim] = useState({ description: '', claimAmount: '' })
  const load = () => api.policy(id).then(r => setP(r.data))
  useEffect(() => { load() }, [id])
  const flash = (ok, text) => { setMsg({ ok, text }); setTimeout(() => setMsg(null), 3000) }
  const act = async (fn, ok) => { try { const r = await fn(); flash(true, ok || r.data?.msg || 'OK'); load(); onChanged() } catch (e) { flash(false, e.message) } }
  if (!p) return <Modal title="…" onClose={onClose}><p className="muted">Đang tải…</p></Modal>
  return (
    <Modal title={`${p.code} — ${p.customerName}`} onClose={onClose} wide>
      <Flash msg={msg} />
      <div className="row" style={{ marginBottom: 8 }}><Badge text={p.statusText} css={p.statusCss} /><span className="pill" style={{ flex: 'none' }}>{p.typeText}</span></div>
      <dl className="dl">
        <dt>Xe</dt><dd>{p.vehiclePlate} · {p.vehicleModel || '—'}</dd><dt>Công ty BH</dt><dd>{p.insurer}</dd>
        <dt>Số tiền BH</dt><dd>{fmtMoney(p.sumInsured)}</dd><dt>Phí</dt><dd>{fmtMoney(p.premium)} (đã thu {fmtMoney(p.paid)})</dd>
        <dt>Hiệu lực</dt><dd>{fmtDate(p.startDate)} → {fmtDate(p.endDate)}</dd>
      </dl>
      {p.status === 0 && (
        <div className="card" style={{ background: '#f8fafc' }}><div className="row">
          <Field label="Thu phí"><input type="number" value={pay} onChange={e => setPay(e.target.value)} placeholder={p.premium} /></Field>
          <div style={{ flex: 'none', alignSelf: 'flex-end' }}><button className="btn sm" onClick={() => act(() => api.receipt(id, { amount: Number(pay || p.premium) }))}>Ghi thu phí</button></div>
        </div></div>
      )}
      {p.receipts.length > 0 && <><div className="section-t">Biên nhận</div><table><tbody>{p.receipts.map((r, i) => <tr key={i}><td>{fmtDate(r.paidAt)}</td><td>{r.method}</td><td className="right">{fmtMoney(r.amount)}</td></tr>)}</tbody></table></>}
      <div className="section-t">Bồi thường</div>
      <table><tbody>{p.claims.map(c => (
        <tr key={c.id}><td>{c.code}</td><td>{c.description}</td><td className="right">{fmtMoney(c.claimAmount)}</td>
          <td><Badge text={c.statusText} css={c.statusCss} /></td>
          <td className="right">{c.status === 0 && <><button className="btn sm" style={{ flex: 'none' }} onClick={() => act(() => api.claimStatus(c.id, 1))}>Duyệt</button> <button className="btn gray sm" style={{ flex: 'none' }} onClick={() => act(() => api.claimStatus(c.id, 2))}>Từ chối</button></>}
            {c.status === 1 && <button className="btn sm" style={{ flex: 'none' }} onClick={() => act(() => api.claimStatus(c.id, 3))}>Chi trả</button>}</td></tr>))}</tbody></table>
      <div className="card" style={{ background: '#fffbeb', marginTop: 10 }}><div className="row">
        <Field label="Mô tả sự cố"><input value={claim.description} onChange={e => setClaim({ ...claim, description: e.target.value })} /></Field>
        <Field label="Số tiền YC"><input type="number" value={claim.claimAmount} onChange={e => setClaim({ ...claim, claimAmount: e.target.value })} /></Field>
        <div style={{ flex: 'none', alignSelf: 'flex-end' }}><button className="btn sm" onClick={() => act(async () => { const r = await api.fileClaim(id, { description: claim.description, claimAmount: Number(claim.claimAmount || 0) }); setClaim({ description: '', claimAmount: '' }); return r }, 'Đã khai báo bồi thường.')}>+ Bồi thường</button></div>
      </div></div>
      {p.status !== 3 && <div style={{ marginTop: 12 }}><button className="btn gray sm" onClick={() => act(() => api.cancel(id), 'Đã hủy HĐ.')}>Hủy hợp đồng</button></div>}
    </Modal>
  )
}

function PolicyForm({ onClose, onSaved }) {
  const [insurers, setInsurers] = useState([])
  const [f, setF] = useState({ customerName: '', customerPhone: '', vehiclePlate: '', vehicleModel: '', insurerId: '', type: 0, sumInsured: 0, premium: 0 })
  const [err, setErr] = useState('')
  useEffect(() => { api.insurers().then(r => { setInsurers(r.data); if (r.data[0]) setF(s => ({ ...s, insurerId: r.data[0].id })) }) }, [])
  const up = (k, v) => setF({ ...f, [k]: v })
  const save = async () => {
    try {
      await api.createPolicy({ ...f, insurerId: Number(f.insurerId), type: Number(f.type), sumInsured: Number(f.sumInsured), premium: Number(f.premium) })
      onSaved()
    } catch (e) { setErr(e.message) }
  }
  return (
    <Modal title="Tạo hợp đồng bảo hiểm" onClose={onClose} wide>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <div className="row"><Field label="Khách hàng *"><input value={f.customerName} onChange={e => up('customerName', e.target.value)} /></Field>
        <Field label="SĐT"><input value={f.customerPhone} onChange={e => up('customerPhone', e.target.value)} /></Field></div>
      <div className="row"><Field label="Biển số *"><input value={f.vehiclePlate} onChange={e => up('vehiclePlate', e.target.value)} /></Field>
        <Field label="Dòng xe"><input value={f.vehicleModel} onChange={e => up('vehicleModel', e.target.value)} /></Field></div>
      <div className="row"><Field label="Công ty BH"><select value={f.insurerId} onChange={e => up('insurerId', e.target.value)}>{insurers.map(i => <option key={i.id} value={i.id}>{i.name}</option>)}</select></Field>
        <Field label="Loại BH"><select value={f.type} onChange={e => up('type', e.target.value)}>{TYPES.map((t, i) => <option key={i} value={i}>{t}</option>)}</select></Field></div>
      <div className="row"><Field label="Số tiền BH"><input type="number" value={f.sumInsured} onChange={e => up('sumInsured', e.target.value)} /></Field>
        <Field label="Phí BH"><input type="number" value={f.premium} onChange={e => up('premium', e.target.value)} /></Field></div>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Tạo (Chờ đóng phí)</button></div>
    </Modal>
  )
}

function Insurers() {
  const [rows, setRows] = useState([]); const [f, setF] = useState({ name: '', code: '', hotline: '' }); const [err, setErr] = useState('')
  const load = () => api.insurers().then(r => setRows(r.data))
  useEffect(() => { load() }, [])
  const add = async () => { try { if (!f.name) return; await api.createInsurer(f); setF({ name: '', code: '', hotline: '' }); load() } catch (e) { setErr(e.message) } }
  return (
    <>
      <h1>Công ty bảo hiểm</h1>{err && <Flash msg={{ ok: false, text: err }} />}
      <div className="card"><div className="row">
        <Field label="Mã"><input value={f.code} onChange={e => setF({ ...f, code: e.target.value })} /></Field>
        <Field label="Tên *"><input value={f.name} onChange={e => setF({ ...f, name: e.target.value })} /></Field>
        <Field label="Hotline"><input value={f.hotline} onChange={e => setF({ ...f, hotline: e.target.value })} /></Field>
        <div style={{ flex: 'none', alignSelf: 'flex-end' }}><button className="btn" onClick={add}>+ Thêm</button></div>
      </div></div>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Mã</th><th>Tên</th><th>Hotline</th></tr></thead>
          <tbody>{rows.map(i => <tr key={i.id}><td>{i.code}</td><td>{i.name}</td><td>{i.hotline || '—'}</td></tr>)}</tbody></table>
      </div>
    </>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="policies" element={<Policies />} />
        <Route path="insurers" element={<Insurers />} />
      </Route>
    </Routes>
  )
}
