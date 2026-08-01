const API_ROOT = import.meta.env.VITE_API_URL || ''

export async function api(path, options = {}) {
  const token = localStorage.getItem('factory_service_token')
  const headers = new Headers(options.headers || {})
  if (options.body && !(options.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  if (token) headers.set('Authorization', `Bearer ${token}`)
  const response = await fetch(`${API_ROOT}${path}`, { ...options, headers, credentials: 'include' })
  if (response.status === 401) {
    localStorage.removeItem('factory_service_token')
    window.dispatchEvent(new Event('factory-auth-expired'))
  }
  const type = response.headers.get('content-type') || ''
  const data = type.includes('json') ? await response.json() : await response.text()
  if (!response.ok) throw Object.assign(new Error(data?.message || `Request failed (${response.status})`), { status: response.status, data })
  return data
}

export async function download(path, filename) {
  const token = localStorage.getItem('factory_service_token')
  const response = await fetch(`${API_ROOT}${path}`, { headers: { Authorization: `Bearer ${token}` } })
  if (!response.ok) throw new Error('ექსპორტი ვერ მომზადდა')
  const blob = await response.blob()
  const link = document.createElement('a')
  link.href = URL.createObjectURL(blob)
  link.download = filename
  link.click()
  URL.revokeObjectURL(link.href)
}
