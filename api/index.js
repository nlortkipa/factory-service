import crypto from 'node:crypto'
import { getDirectoryStore } from './directory-store.js'

const plantId = '78f731db-9089-4b2e-87a3-3fc1b24f4685'
const operatorRecordId = 'a2d623d3-87ac-4208-8cfa-39892988944c'
const configuredOperatorId = '07IdLcVyn4q-s77Or8GldZwg_BsoQzSM'
const adminId = '8ba2d8b7-a558-4372-ae92-78cbe9428549'
const serviceTypes = ['Sales', 'Scale', 'Loading']
const signingKey = process.env.QR_SIGNING_KEY || 'factory-service-vercel-test-signing-key-change-before-production'
const repeatWindowDays = 7
const repeatWindowMs = repeatWindowDays * 86400000
const qrLifetimeMs = 60 * 1000
const surveyLifetimeMs = 20 * 60 * 1000
const deviceCookieName = 'factory_service_device'

let plants = [{ id: plantId, name: 'კასპის ცემენტის ქარხანა', address: 'კასპი, საქართველო', timeZone: 'Asia/Tbilisi' }]
let operators = [{ id: operatorRecordId, operatorId: configuredOperatorId, displayName: 'ტესტ ოპერატორი', plantId, plantName: plants[0].name }]
let settings = {
  repeatWindowDays: '7',
  commentMaxLength: '1000',
  lowScoreThreshold: '2',
  privacyNotice: 'სატესტო განთავსება: მონაცემები პერიოდულად განულდება.'
}
let audits = []
let ratings = createDemoRatings()
const idempotencyKeys = new Map()
const completedSessions = new Set()
const deviceResponses = []

function createDemoRatings() {
  const scores = [[5, 4, 5], [4, 4, 3], [5, 5, 4], [3, 4, 4], [4, 5, 5]]
  return scores.flatMap((values, responseIndex) => {
    const responseId = crypto.randomUUID()
    const submittedAt = new Date(Date.now() - (responseIndex + 1) * 86400000 * 2).toISOString()
    return serviceTypes.map((serviceType, index) => ({
      id: crypto.randomUUID(), responseId, submittedAt, plantId, plant: plants[0].name,
      operatorId: operatorRecordId, operator: operators[0].displayName, serviceType,
      score: values[index], comment: responseIndex === 1 && index === 0 ? 'მომსახურება სწრაფი იყო.' : ''
    }))
  })
}

function requestPath(request) {
  const raw = Array.isArray(request.query?.path) ? request.query.path.join('/') : request.query?.path
  if (raw) return String(raw).replace(/^\/+|\/+$/g, '')
  return new URL(request.url, 'http://localhost').pathname.replace(/^\/api\/?/, '').replace(/^\/+|\/+$/g, '')
}

function body(request) {
  if (typeof request.body === 'string') {
    try { return JSON.parse(request.body) } catch { return {} }
  }
  return request.body || {}
}

function json(response, status, value) {
  response.status(status).setHeader('Content-Type', 'application/json; charset=utf-8')
  return response.end(JSON.stringify(value))
}

function noContent(response) {
  response.status(204)
  return response.end()
}

function audit(action, objectType, objectId) {
  audits.unshift({
    id: crypto.randomUUID(), actorId: adminId, action, objectType, objectId,
    beforeJson: '', afterJson: '', ipAddress: '', timestamp: new Date().toISOString(),
    correlationId: crypto.randomUUID()
  })
  audits = audits.slice(0, 500)
}

function sign(payload) {
  const encoded = Buffer.from(JSON.stringify(payload)).toString('base64url')
  const signature = crypto.createHmac('sha256', signingKey).update(encoded).digest('base64url')
  return `${encoded}.${signature}`
}

function verify(token) {
  const [encoded, signature] = String(token || '').split('.', 2)
  if (!encoded || !signature) return null
  const expected = crypto.createHmac('sha256', signingKey).update(encoded).digest()
  let actual
  try { actual = Buffer.from(signature, 'base64url') } catch { return null }
  if (actual.length !== expected.length || !crypto.timingSafeEqual(actual, expected)) return null
  try { return JSON.parse(Buffer.from(encoded, 'base64url').toString('utf8')) } catch { return null }
}

function publicBaseUrl(request) {
  if (process.env.PUBLIC_BASE_URL) return process.env.PUBLIC_BASE_URL.replace(/\/$/, '')
  const protocol = request.headers['x-forwarded-proto'] || 'https'
  return `${protocol}://${request.headers.host}`
}

function getOrSetDeviceToken(request, response) {
  const cookies = String(request.headers.cookie || '').split(';')
  const existing = cookies.map(value => value.trim().split('=')).find(([name]) => name === deviceCookieName)?.[1]
  if (existing && /^[A-Za-z0-9_-]{20,200}$/.test(existing)) return existing

  const token = crypto.randomBytes(32).toString('base64url')
  const secure = Boolean(process.env.VERCEL) || String(request.headers['x-forwarded-proto'] || '').includes('https')
  response.setHeader('Set-Cookie', `${deviceCookieName}=${token}; Path=/; Max-Age=31536000; HttpOnly; SameSite=Lax${secure ? '; Secure' : ''}`)
  return token
}

function deviceHash(token) {
  return crypto.createHash('sha256').update(token).digest('hex')
}

function nextAllowedAt(submittedAt) {
  return new Date(new Date(submittedAt).getTime() + repeatWindowMs).toISOString()
}

function normalizedRatings(items) {
  return items.map(item => ({ ...item, submittedAt: new Date(item.submittedAt).toISOString(), score: Number(item.score) }))
}

function filteredRatings(items, query) {
  const from = query.from ? new Date(query.from).getTime() : Date.now() - 30 * 86400000
  const to = query.to ? new Date(query.to).getTime() + 86400000 : Date.now() + 86400000
  return items.filter(item => {
    const time = new Date(item.submittedAt).getTime()
    return time >= from && time < to
      && (!query.plantId || item.plantId === query.plantId)
      && (!query.operatorId || (item.operatorId === query.operatorId && item.serviceType === 'Sales'))
      && (!query.serviceType || item.serviceType === query.serviceType)
      && (!query.score || item.score === Number(query.score))
      && (!query.search || item.comment.toLocaleLowerCase().includes(String(query.search).toLocaleLowerCase()))
  })
}

function metric(items, type) {
  const values = items.filter(item => item.serviceType === type)
  const count = values.length
  const average = count ? Math.round(values.reduce((sum, item) => sum + item.score, 0) / count * 100) / 100 : 0
  const lowPercent = count ? Math.round(values.filter(item => item.score <= 2).length * 1000 / count) / 10 : 0
  return { average, count, lowPercent, distribution: [1, 2, 3, 4, 5].map(score => ({ score, count: values.filter(item => item.score === score).length })) }
}

function dashboard(allRatings, query) {
  const items = filteredRatings(allRatings, query)
  const responses = [...new Set(items.map(item => item.responseId))]
  const trendGroups = Map.groupBy(items, item => item.submittedAt.slice(0, 10))
  const trend = [...trendGroups.entries()].sort(([left], [right]) => left.localeCompare(right)).map(([date, group]) => ({
    date, average: Math.round(group.reduce((sum, item) => sum + item.score, 0) / group.length * 100) / 100,
    count: new Set(group.map(item => item.responseId)).size
  }))
  const plantGroups = Map.groupBy(items, item => item.plantId)
  const plantMetrics = [...plantGroups.entries()].map(([id, group]) => ({
    id, name: group[0].plant, average: Math.round(group.reduce((sum, item) => sum + item.score, 0) / group.length * 100) / 100,
    count: new Set(group.map(item => item.responseId)).size
  })).sort((left, right) => right.average - left.average)
  return { total: responses.length, uniqueDevices: responses.length, sales: metric(items, 'Sales'), scale: metric(items, 'Scale'), loading: metric(items, 'Loading'), trend, plants: plantMetrics, blockedAttempts: 0 }
}

function exportCsv(items) {
  const escape = value => `"${String(value ?? '').replaceAll('"', '""')}"`
  const rows = [['Submitted at', 'Plant', 'Operator', 'Service', 'Score', 'Comment'], ...items.map(item => [item.submittedAt, item.plant, item.operator, item.serviceType, item.score, item.comment])]
  return '\ufeff' + rows.map(row => row.map(escape).join(',')).join('\r\n')
}

function parseGuidPath(path, prefix) {
  return decodeURIComponent(path.slice(prefix.length)).replace(/^\/+|\/+$/g, '')
}

export default async function handler(request, response) {
  response.setHeader('Cache-Control', 'no-store')
  response.setHeader('X-Content-Type-Options', 'nosniff')
  response.setHeader('Referrer-Policy', 'same-origin')
  const path = requestPath(request)
  const method = request.method || 'GET'
  const input = body(request)
  const directoryStore = getDirectoryStore()
  const needsDirectory = path === 'agent/qr' || path.startsWith('survey/') || path === 'admin/dashboard' || path === 'admin/responses' || path.startsWith('admin/exports/') || path === 'admin/plants' || path.startsWith('admin/plants/') || path === 'admin/operators' || path.startsWith('admin/operators/')

  if (process.env.VERCEL && needsDirectory && !directoryStore) {
    console.error('[directory] DATABASE_URL is not configured')
    return json(response, 503, { message: 'მონაცემთა ბაზა არ არის კონფიგურირებული. მიუთითეთ DATABASE_URL.' })
  }

  if (path === 'health' && method === 'GET') return json(response, 200, { status: 'ok', mode: 'vercel-node-adapter', directoryStorage: directoryStore ? 'postgres' : 'memory', time: new Date().toISOString() })
  if (path === 'auth/me' && method === 'GET') return json(response, 200, { id: adminId, username: 'admin', displayName: 'Test Administrator', role: 'SystemAdministrator', plantId: null, plant: null })
  if (path === 'auth/login' && method === 'POST') return json(response, 200, { challengeId: crypto.randomUUID(), phoneMasked: '+995\u2022\u2022\u2022001', expiresAt: new Date(Date.now() + 300000).toISOString() })
  if (path === 'auth/verify' && method === 'POST') return json(response, 200, { token: 'test-admin', user: { id: adminId, username: 'admin', displayName: 'Test Administrator', role: 'SystemAdministrator' } })
  if (path === 'auth/logout' && method === 'POST') return noContent(response)

  if (path === 'agent/qr' && method === 'POST') {
    const requestedOperatorId = String(input.operatorId || '').trim()
    const factoryOperator = directoryStore
      ? await directoryStore.findOperatorByOperatorId(requestedOperatorId)
      : operators.find(item => item.operatorId === requestedOperatorId)
    if (!factoryOperator) return json(response, 404, { code: 'OPERATOR_NOT_FOUND', message: 'ოპერატორი ვერ მოიძებნა.' })
    const now = Date.now()
    const payload = { sessionId: crypto.randomUUID(), operatorId: factoryOperator.id, plantId: factoryOperator.plantId, operatorName: factoryOperator.displayName, plantName: factoryOperator.plantName, issuedAt: now, expiresAt: now + qrLifetimeMs, surveyExpiresAt: now + surveyLifetimeMs }
    const token = sign(payload)
    return json(response, 200, { token, surveyUrl: `${publicBaseUrl(request)}/s/${encodeURIComponent(token)}`, expiresAt: new Date(payload.expiresAt).toISOString(), operatorName: payload.operatorName, plantName: payload.plantName })
  }

  if (path.startsWith('survey/')) {
    const token = decodeURIComponent(parseGuidPath(path, 'survey/'))
    const payload = verify(token)
    if (!payload) return json(response, 400, { code: 'QR_INVALID', message: 'QR კოდი არასწორია.' })
    if (method === 'GET') {
      if (payload.expiresAt <= Date.now()) return json(response, 400, { code: 'QR_EXPIRED', message: 'QR კოდს ვადა გაუვიდა. გთხოვთ თავიდან დაასკანეროთ.' })
      const hash = deviceHash(getOrSetDeviceToken(request, response))
      const cutoff = new Date(Date.now() - repeatWindowMs).toISOString()
      const recent = directoryStore
        ? await directoryStore.findRecentResponse(hash, cutoff)
        : deviceResponses.filter(item => item.deviceTokenHash === hash && new Date(item.submittedAt) >= new Date(cutoff)).sort((a, b) => b.submittedAt.localeCompare(a.submittedAt))[0]
      return json(response, 200, { sessionId: payload.sessionId, plant: payload.plantName, operatorName: payload.operatorName, surveyExpiresAt: new Date(payload.surveyExpiresAt).toISOString(), blocked: Boolean(recent), nextAllowedAt: recent ? nextAllowedAt(recent.submittedAt) : null, privacyNotice: settings.privacyNotice })
    }
    if (method === 'POST') {
      if (payload.surveyExpiresAt <= Date.now()) return json(response, 400, { code: 'SURVEY_EXPIRED', message: 'შეფასების სესიას ვადა გაუვიდა.' })
      if (!Array.isArray(input.ratings) || input.ratings.length !== 3 || new Set(input.ratings.map(item => item.serviceType)).size !== 3 || input.ratings.some(item => !serviceTypes.includes(item.serviceType) || item.score < 1 || item.score > 5)) return json(response, 400, { message: 'სამივე შეფასება სავალდებულოა.' })
      const idempotencyKey = String(input.idempotencyKey || '').trim()
      if (!idempotencyKey || idempotencyKey.length > 100) return json(response, 400, { message: 'შეფასების მოთხოვნა არასწორია.' })
      const duplicate = directoryStore ? await directoryStore.findResponseByIdempotencyKey(idempotencyKey) : (idempotencyKeys.has(idempotencyKey) ? { id: idempotencyKeys.get(idempotencyKey), submittedAt: new Date().toISOString() } : null)
      if (duplicate) return json(response, 200, { responseId: duplicate.id, duplicate: true, nextAllowedAt: nextAllowedAt(duplicate.submittedAt) })
      const hash = deviceHash(getOrSetDeviceToken(request, response))
      const cutoff = new Date(Date.now() - repeatWindowMs).toISOString()
      const recent = directoryStore
        ? await directoryStore.findRecentResponse(hash, cutoff)
        : deviceResponses.filter(item => item.deviceTokenHash === hash && new Date(item.submittedAt) >= new Date(cutoff)).sort((a, b) => b.submittedAt.localeCompare(a.submittedAt))[0]
      if (recent) return json(response, 429, { code: 'REPEAT_BLOCKED', message: 'შეფასება უკვე დაფიქსირებულია.', nextAllowedAt: nextAllowedAt(recent.submittedAt) })
      if (!directoryStore && completedSessions.has(payload.sessionId)) return json(response, 409, { code: 'SESSION_COMPLETED', message: 'ამ QR კოდით შეფასება უკვე დაფიქსირებულია.' })
      const responseId = crypto.randomUUID()
      const submittedAt = new Date().toISOString()
      const cleanRatings = input.ratings.map(item => ({ id: crypto.randomUUID(), serviceType: item.serviceType, score: Number(item.score), comment: String(item.comment || '').replace(/<[^>]+>/g, '').slice(0, 1000) }))
      if (directoryStore) {
        const created = await directoryStore.createResponse({ id: responseId, sessionId: payload.sessionId, deviceTokenHash: hash, submittedAt, plantId: payload.plantId, operatorId: payload.operatorId, idempotencyKey }, cleanRatings, cutoff)
        if (!created) {
          const latest = await directoryStore.findRecentResponse(hash, cutoff)
          if (latest) return json(response, 429, { code: 'REPEAT_BLOCKED', message: 'შეფასება უკვე დაფიქსირებულია.', nextAllowedAt: nextAllowedAt(latest.submittedAt) })
          return json(response, 409, { code: 'SESSION_COMPLETED', message: 'ამ QR კოდით შეფასება უკვე დაფიქსირებულია.' })
        }
      } else {
        for (const item of cleanRatings) ratings.push({ ...item, responseId, submittedAt, plantId: payload.plantId, plant: payload.plantName, operatorId: payload.operatorId, operator: payload.operatorName })
        deviceResponses.push({ responseId, deviceTokenHash: hash, submittedAt })
        idempotencyKeys.set(idempotencyKey, responseId)
        completedSessions.add(payload.sessionId)
      }
      return json(response, 200, { responseId, duplicate: false, nextAllowedAt: nextAllowedAt(submittedAt), message: 'გმადლობთ შეფასებისთვის!' })
    }
  }

  if (path === 'admin/dashboard' && method === 'GET') {
    const allRatings = directoryStore ? normalizedRatings(await directoryStore.listRatings()) : ratings
    return json(response, 200, dashboard(allRatings, request.query || {}))
  }
  if (path === 'admin/responses' && method === 'GET') {
    const allRatings = directoryStore ? normalizedRatings(await directoryStore.listRatings()) : ratings
    const items = filteredRatings(allRatings, request.query || {})
    const page = Math.max(1, Number(request.query.page || 1))
    const pageSize = Math.min(200, Math.max(1, Number(request.query.pageSize || 50)))
    return json(response, 200, { total: items.length, page, pageSize, items: items.sort((a, b) => b.submittedAt.localeCompare(a.submittedAt)).slice((page - 1) * pageSize, page * pageSize) })
  }
  if (path.startsWith('admin/exports/') && method === 'GET') {
    const format = parseGuidPath(path, 'admin/exports/').toLowerCase()
    const allRatings = directoryStore ? normalizedRatings(await directoryStore.listRatings()) : ratings
    const content = exportCsv(filteredRatings(allRatings, request.query || {}))
    response.status(200).setHeader('Content-Type', format === 'xlsx' ? 'application/vnd.ms-excel; charset=utf-8' : 'text/csv; charset=utf-8')
    response.setHeader('Content-Disposition', `attachment; filename="factory-service-report.${format === 'xlsx' ? 'xls' : 'csv'}"`)
    return response.end(content)
  }
  if (path === 'admin/plants' && method === 'GET') return json(response, 200, directoryStore ? await directoryStore.listPlants() : plants)
  if (path === 'admin/plants' && method === 'POST') {
    const entity = { id: crypto.randomUUID(), name: String(input.name || '').trim(), address: String(input.address || '').trim(), timeZone: String(input.timeZone || 'Asia/Tbilisi').trim() }
    const created = directoryStore ? await directoryStore.createPlant(entity) : (plants.push(entity), entity)
    audit('Create', 'Plant', created.id); return json(response, 201, created)
  }
  if (path.startsWith('admin/plants/')) {
    const id = parseGuidPath(path, 'admin/plants/')
    const entity = directoryStore ? await directoryStore.findPlant(id) : plants.find(item => item.id === id)
    if (!entity) return json(response, 404, { message: 'ქარხანა ვერ მოიძებნა.' })
    if (method === 'PUT') {
      const changes = { ...entity, name: String(input.name || '').trim(), address: String(input.address || '').trim(), timeZone: String(input.timeZone || '').trim() }
      const updated = directoryStore ? await directoryStore.updatePlant(changes) : Object.assign(entity, changes)
      if (!directoryStore) operators = operators.map(item => item.plantId === id ? { ...item, plantName: updated.name } : item)
      audit('Update', 'Plant', id); return json(response, 200, updated)
    }
    if (method === 'DELETE') {
      const hasOperators = directoryStore ? await directoryStore.plantHasOperators(id) : operators.some(item => item.plantId === id)
      if (hasOperators) return json(response, 409, { message: 'ქარხანა დაკავშირებულია ოპერატორთან.' })
      if (directoryStore) await directoryStore.deletePlant(id)
      else plants = plants.filter(item => item.id !== id)
      audit('Delete', 'Plant', id); return noContent(response)
    }
  }
  if (path === 'admin/operators' && method === 'GET') return json(response, 200, directoryStore ? await directoryStore.listOperators() : operators)
  if (path === 'admin/operators' && method === 'POST') {
    const plant = directoryStore ? await directoryStore.findPlant(input.plantId) : plants.find(item => item.id === input.plantId)
    if (!plant) return json(response, 400, { message: 'მიუთითეთ ქარხანა.' })
    const entity = { id: crypto.randomUUID(), operatorId: crypto.randomBytes(24).toString('base64url'), displayName: String(input.displayName || '').trim(), plantId: plant.id, plantName: plant.name }
    const created = directoryStore ? await directoryStore.createOperator(entity) : (operators.push(entity), entity)
    audit('Create', 'Operator', created.id); return json(response, 201, created)
  }
  if (path.startsWith('admin/operators/')) {
    const id = parseGuidPath(path, 'admin/operators/')
    const entity = directoryStore ? await directoryStore.findOperator(id) : operators.find(item => item.id === id)
    if (!entity) return json(response, 404, { message: 'ოპერატორი ვერ მოიძებნა.' })
    if (method === 'PUT') {
      const plant = directoryStore ? await directoryStore.findPlant(input.plantId) : plants.find(item => item.id === input.plantId)
      if (!plant) return json(response, 400, { message: 'მიუთითეთ ქარხანა.' })
      const changes = { ...entity, displayName: String(input.displayName || '').trim(), plantId: plant.id, plantName: plant.name }
      const updated = directoryStore ? await directoryStore.updateOperator(changes) : Object.assign(entity, changes)
      audit('Update', 'Operator', id); return json(response, 200, updated)
    }
    if (method === 'DELETE') {
      const hasRatings = directoryStore ? await directoryStore.operatorHasRatings(id) : ratings.some(item => item.operatorId === id)
      if (hasRatings) return json(response, 409, { message: 'ოპერატორი დაკავშირებულია შეფასებებთან და ვერ წაიშლება.' })
      if (directoryStore) await directoryStore.deleteOperator(id)
      else operators = operators.filter(item => item.id !== id)
      audit('Delete', 'Operator', id); return noContent(response)
    }
  }
  if (path === 'admin/settings' && method === 'GET') return json(response, 200, settings)
  if (path === 'admin/settings' && method === 'PUT') { settings = { ...settings, ...Object.fromEntries(Object.entries(input).map(([key, value]) => [key, String(value)])) }; audit('Update', 'ApplicationSettings', 'global'); return noContent(response) }
  if (path === 'admin/audit' && method === 'GET') return json(response, 200, audits.slice(0, Math.min(500, Math.max(1, Number(request.query.take || 100)))))

  return json(response, 404, { message: `Route not found: ${method} /${path}` })
}
