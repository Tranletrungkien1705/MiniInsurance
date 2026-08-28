const base = '/api/v1'
async function req(path, opts = {}) {
  const res = await fetch(base + path, {
    headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin',
    ...opts, body: opts.body ? JSON.stringify(opts.body) : undefined
  })
  const text = await res.text(); const data = text ? JSON.parse(text) : null
  if (!res.ok) throw new Error(data?.error || `Lỗi ${res.status}`)
  return { data, cache: res.headers.get('X-Cache') }
}
export const api = {
  dashboard: () => req('/dashboard'),
  insurers: () => req('/insurers'),
  createInsurer: (b) => req('/insurers', { method: 'POST', body: b }),
  policies: (status, q) => req(`/policies?${status != null ? `status=${status}&` : ''}${q ? `q=${encodeURIComponent(q)}` : ''}`),
  policy: (id) => req(`/policies/${id}`),
  createPolicy: (b) => req('/policies', { method: 'POST', body: b }),
  receipt: (id, b) => req(`/policies/${id}/receipt`, { method: 'POST', body: b }),
  cancel: (id) => req(`/policies/${id}/cancel`, { method: 'POST' }),
  fileClaim: (id, b) => req(`/policies/${id}/claims`, { method: 'POST', body: b }),
  claimStatus: (cid, status) => req(`/claims/${cid}/status`, { method: 'POST', body: { status } })
}
export const fmtMoney = (n) => (n ?? 0).toLocaleString('vi-VN') + ' ₫'
export const fmtDate = (s) => s ? new Date(s).toLocaleDateString('vi-VN') : '—'
export const TYPES = ['TNDS bắt buộc', 'Vật chất thân vỏ', 'Tai nạn người ngồi']
export const STATUS = ['Chờ đóng phí', 'Hiệu lực', 'Hết hạn', 'Đã hủy']
export const CLAIMS = ['Đã khai báo', 'Đã duyệt', 'Từ chối', 'Đã chi trả']
