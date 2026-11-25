import crypto from 'node:crypto'
import { getDirectoryStore } from './directory-store.js'
import { createQrToken, readQrToken } from './qr-token.js'

const plantId = '78f731db-9089-4b2e-87a3-3fc1b24f4685'
const operatorRecordId = 'a2d623d3-87ac-4208-8cfa-39892988944c'
const configuredOperatorId = '07IdLcVyn4q-s77Or8GldZwg_BsoQzSM'
const adminId = '8ba2d8b7-a558-4372-ae92-78cbe9428549'
const serviceTypes = ['Sales', 'Scale', 'Loading']
const signingKey = process.env.QR_SIGNING_KEY || 'factory-service-vercel-test-signing-key-change-before-production'
const smsKey = (process.env.SMS_KEY || '').trim()
const smsSender = (process.env.SMS_SENDER || 'Beton.ge').trim()
const authSigningKey = process.env.AUTH_SIGNING_KEY || smsKey || signingKey
const adminUsername = process.env.ADMIN_USERNAME || 'admin'
const adminPhone = (process.env.ADMIN_PHONE || '').trim()
const adminDisplayName = process.env.ADMIN_DISPLAY_NAME || 'System Administrator'
const qrLifetimeMs = 5 * 60 * 1000
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
const mfaAttempts = new Map()

function createDemoRatings() {
  const scores = [[5, 4, 5], [4, 4, 3], [5, 5, 4], [3, 4, 4], [4, 5, 5]]
  const carPlates = ['AA123BB', 'TB450KL', 'GE777GE', 'ZZ018AC', 'KM302RT']
  return scores.flatMap((values, responseIndex) => {
    const responseId = crypto.randomUUID()
    const submittedAt = new Date(Date.now() - (responseIndex + 1) * 86400000 * 2).toISOString()
    return serviceTypes.map((serviceType, index) => ({
      id: crypto.randomUUID(), responseId, submittedAt, carPlate: carPlates[responseIndex], plantId, plant: plants[0].name,
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

function signWithKey(payload, key) {
  const encoded = Buffer.from(JSON.stringify(payload)).toString('base64url')
  const signature = crypto.createHmac('sha256', key).update(encoded).digest('base64url')
  return `${encoded}.${signature}`
}

function verifyWithKey(token, key) {
  const [encoded, signature] = String(token || '').split('.', 2)
  if (!encoded || !signature) return null
  const expected = crypto.createHmac('sha256', key).update(encoded).digest()
  let actual
  try { actual = Buffer.from(signature, 'base64url') } catch { return null }
  if (actual.length !== expected.length || !crypto.timingSafeEqual(actual, expected)) return null
  try { return JSON.parse(Buffer.from(encoded, 'base64url').toString('utf8')) } catch { return null }
}

function fixedEquals(left, right) {
  const leftBuffer = Buffer.from(String(left))
  const rightBuffer = Buffer.from(String(right))
  return leftBuffer.length === rightBuffer.length && crypto.timingSafeEqual(leftBuffer, rightBuffer)
}

function maskPhone(phone) {
  const digits = String(phone).replace(/\D/g, '')
  const local = digits.startsWith('995') && digits.length === 12 ? digits.slice(3) : digits
  return local.length >= 3 ? `+995\u2022\u2022\u2022${local.slice(-3)}` : '\u2022\u2022\u2022'
}

function smsDestination(phone) {
  const digits = String(phone).replace(/\D/g, '')
  return digits.length === 9 && digits.startsWith('5') ? `995${digits}` : digits
}

async function sendSmsCode(phone, code) {
  if (!smsKey) throw new Error('SMS_KEY is not configured')
  if (!smsSender || smsSender.length > 11) throw new Error(`SMS_SENDER must be a registered sender with at most 11 characters (received ${smsSender.length})`)
  const destination = smsDestination(phone)
  if (!/^9955\d{8}$/.test(destination)) throw new Error('ADMIN_PHONE is not configured')
  const url = new URL('https://smsoffice.ge/api/v2/send/')
  url.searchParams.set('key', smsKey)
  url.searchParams.set('destination', destination)
  url.searchParams.set('sender', smsSender)
  url.searchParams.set('content', code)
  url.searchParams.set('urgent', 'true')
  const result = await fetch(url, { signal: AbortSignal.timeout(10000) })
  if (!result.ok) throw new Error(`SMS provider returned HTTP ${result.status}`)
  const payload = await result.json().catch(() => null)
  if (!payload?.Success || Number(payload.ErrorCode) !== 0) {
    throw new Error(`SMS provider rejected the request (code ${payload?.ErrorCode ?? 'unknown'}): ${payload?.Message || 'Unknown error'}`)
  }
}

function createMfaChallenge(code) {
  const nonce = crypto.randomBytes(18).toString('base64url')
  const expiresAt = Date.now() + 5 * 60 * 1000
  const codeMac = crypto.createHmac('sha256', authSigningKey).update(`${nonce}:${code}`).digest('base64url')
  const challengeId = signWithKey({ type: 'admin-mfa', username: adminUsername, nonce, codeMac, expiresAt }, authSigningKey)
  return { challengeId, nonce, expiresAt }
}

function authenticatedAdmin(request) {
  const authorization = String(request.headers.authorization || '')
  if (!authorization.toLowerCase().startsWith('bearer ')) return null
  const payload = verifyWithKey(authorization.slice(7).trim(), authSigningKey)
  return payload?.type === 'admin-session' && payload.username === adminUsername && payload.expiresAt > Date.now() ? payload : null
}

function adminUser() {
  return { id: adminId, username: adminUsername, displayName: adminDisplayName, role: 'SystemAdministrator', plantId: null, plant: null }
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

function nextAllowedAt(submittedAt, repeatWindowMs) {
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
      && (!query.operatorId || item.operatorId === query.operatorId)
      && (!query.serviceType || item.serviceType === query.serviceType)
      && (!query.score || item.score === Number(query.score))
  })
}

function responseRows(items, query) {
  const from = query.from ? new Date(query.from).getTime() : Date.now() - 30 * 86400000
  const to = query.to ? new Date(query.to).getTime() + 86400000 : Date.now() + 86400000
  return [...Map.groupBy(items, item => item.responseId).values()].filter(group => {
    const first = group[0]
    const time = new Date(first.submittedAt).getTime()
    const ratingMatch = item => (!query.serviceType || item.serviceType === query.serviceType)
      && (!query.score || item.score === Number(query.score))
    return time >= from && time < to
      && (!query.plantId || first.plantId === query.plantId)
      && (!query.operatorId || first.operatorId === query.operatorId)
      && ((!query.serviceType && !query.score) || group.some(ratingMatch))
  }).map(group => {
    const first = group[0]
    const rating = type => group.find(item => item.serviceType === type)
    const score = type => rating(type)?.score || 0
    const comment = type => rating(type)?.comment?.trim() || ''
    const comments = [...new Set(group.map(item => item.comment.trim()).filter(Boolean))]
    return {
      id: first.responseId, submittedAt: first.submittedAt, carPlate: first.carPlate || '', plant: first.plant, operator: first.operator,
      loadingScore: score('Loading'), loadingComment: comment('Loading'),
      salesScore: score('Sales'), salesComment: comment('Sales'),
      scaleScore: score('Scale'), scaleComment: comment('Scale'),
      comment: comments.join(' • ')
    }
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
  const rows = [['თარიღი', 'სახელმწიფო ნომერი', 'ქარხანა', 'ოპერატორი', 'დატვირთვის მომსახურება', 'გაყიდვების ოპერატორი', 'სასწორის მომსასხურება', 'კომენტარი'], ...items.map(item => [item.submittedAt, item.carPlate, item.plant, item.operator, item.loadingScore, item.salesScore, item.scaleScore, item.comment])]
  return '\ufeff' + rows.map(row => row.map(escape).join(',')).join('\r\n')
}

export function normalizeCarPlate(value) {
  // Accepts AA123AA or AA-123-AA (any case) and stores it as AA123AA.
  const match = /^([A-Z]{2})-?(\d{3})-?([A-Z]{2})$/.exec(String(value ?? '').trim().toUpperCase())
  return match ? match.slice(1).join('') : null
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
  const needsDirectory = path === 'admin/settings' || path === 'auth/login' || path === 'auth/verify' || path === 'agent/qr' || path.startsWith('survey/') || path === 'admin/dashboard' || path === 'admin/responses' || path.startsWith('admin/responses/') || path.startsWith('admin/exports/') || path === 'admin/plants' || path.startsWith('admin/plants/') || path === 'admin/operators' || path.startsWith('admin/operators/')

  if (process.env.VERCEL && needsDirectory && !directoryStore) {
    console.error('[directory] DATABASE_URL is not configured')
    return json(response, 503, { message: 'მონაცემთა ბაზა არ არის კონფიგურირებული. მიუთითეთ DATABASE_URL.' })
  }

  if (path === 'health' && method === 'GET') return json(response, 200, { status: 'ok', mode: 'vercel-node-adapter', directoryStorage: directoryStore ? 'postgres' : 'memory', time: new Date().toISOString() })
  if (path === 'auth/login' && method === 'POST') {
    const code = crypto.randomInt(0, 1_000_000).toString().padStart(6, '0')
    const challenge = createMfaChallenge(code)
    try {
      await sendSmsCode(adminPhone, code)
      if (directoryStore) await directoryStore.createMfaChallenge({ nonce: challenge.nonce, codeMac: verifyWithKey(challenge.challengeId, authSigningKey).codeMac, expiresAt: new Date(challenge.expiresAt).toISOString() })
    } catch (error) {
      console.error('[sms] Failed to send administrator MFA code:', error instanceof Error ? error.message : 'unknown error')
      return json(response, 502, { message: 'SMS \u10d9\u10dd\u10d3\u10d8\u10e1 \u10d2\u10d0\u10d2\u10d6\u10d0\u10d5\u10dc\u10d0 \u10d5\u10d4\u10e0 \u10db\u10dd\u10ee\u10d4\u10e0\u10ee\u10d3\u10d0. \u10d2\u10d7\u10ee\u10dd\u10d5\u10d7 \u10e1\u10ea\u10d0\u10d3\u10dd\u10d7 \u10ee\u10d4\u10da\u10d0\u10ee\u10da\u10d0.' })
    }
    if (!directoryStore) {
      mfaAttempts.set(challenge.nonce, { attempts: 0, used: false, expiresAt: challenge.expiresAt })
      for (const [nonce, state] of mfaAttempts) if (state.expiresAt <= Date.now()) mfaAttempts.delete(nonce)
    }
    return json(response, 200, { challengeId: challenge.challengeId, phoneMasked: maskPhone(adminPhone), expiresAt: new Date(challenge.expiresAt).toISOString() })
  }
  if (path === 'auth/verify' && method === 'POST') {
    const challenge = verifyWithKey(input.challengeId, authSigningKey)
    if (challenge?.type !== 'admin-mfa' || challenge.username !== adminUsername || challenge.expiresAt <= Date.now() || !/^\d{6}$/.test(String(input.code || ''))) {
      return json(response, 400, { message: '\u10d9\u10dd\u10d3\u10d8 \u10d0\u10e0\u10d0\u10e1\u10ec\u10dd\u10e0\u10d8\u10d0 \u10d0\u10dc \u10d5\u10d0\u10d3\u10d0 \u10d2\u10d0\u10e3\u10d5\u10d8\u10d3\u10d0.' })
    }
    const codeMac = crypto.createHmac('sha256', authSigningKey).update(`${challenge.nonce}:${String(input.code)}`).digest('base64url')
    let verification
    if (directoryStore) {
      verification = await directoryStore.consumeMfaChallenge(challenge.nonce, codeMac)
    } else {
      const state = mfaAttempts.get(challenge.nonce)
      if (state && !state.used && state.attempts < 5) {
        state.attempts += 1
        const valid = fixedEquals(codeMac, challenge.codeMac)
        if (valid) state.used = true
        verification = { valid, attemptsRemaining: 5 - state.attempts }
      }
    }
    if (!verification?.valid) {
      return json(response, 400, { message: '\u10d9\u10dd\u10d3\u10d8 \u10d0\u10e0\u10d0\u10e1\u10ec\u10dd\u10e0\u10d8\u10d0 \u10d0\u10dc \u10d5\u10d0\u10d3\u10d0 \u10d2\u10d0\u10e3\u10d5\u10d8\u10d3\u10d0.', attemptsRemaining: verification?.attemptsRemaining })
    }
    const token = signWithKey({ type: 'admin-session', username: adminUsername, expiresAt: Date.now() + 8 * 60 * 60 * 1000 }, authSigningKey)
    return json(response, 200, { token, user: adminUser() })
  }
  if (path === 'auth/logout' && method === 'POST') return noContent(response)

  const adminSession = path === 'auth/me' || path.startsWith('admin/') ? authenticatedAdmin(request) : null
  if ((path === 'auth/me' || path.startsWith('admin/')) && !adminSession) return json(response, 401, { message: '\u10d0\u10d5\u10e2\u10dd\u10e0\u10d8\u10d6\u10d0\u10ea\u10d8\u10d0 \u10e1\u10d0\u10ed\u10d8\u10e0\u10dd\u10d0.' })
  if (path === 'auth/me' && method === 'GET') return json(response, 200, adminUser())

  if (path === 'agent/qr' && method === 'POST') {
    const requestedOperatorId = String(input.operatorId || '').trim()
    if (!requestedOperatorId || requestedOperatorId.length > 64) return json(response, 400, { code: 'INVALID_OPERATOR_ID', message: 'მიუთითეთ სწორი ოპერატორის ID.' })
    const factoryOperator = directoryStore
      ? await directoryStore.findOperatorByOperatorId(requestedOperatorId)
      : operators.find(item => item.operatorId === requestedOperatorId)
    if (!factoryOperator) return json(response, 404, { code: 'OPERATOR_NOT_FOUND', message: 'ოპერატორი ვერ მოიძებნა.' })
    const now = Date.now()
    const currentToken = typeof input.currentToken === 'string' ? input.currentToken : ''
    let payload = readQrToken(currentToken, signingKey)
    const reusable = payload && payload.operatorId === factoryOperator.id && payload.plantId === factoryOperator.plantId
      && payload.issuedAt <= now && payload.issuedAt + qrLifetimeMs > now
      && !(directoryStore ? await directoryStore.isSessionCompleted(payload.sessionId) : completedSessions.has(payload.sessionId))
    if (!reusable) payload = { sessionId: crypto.randomUUID(), operatorId: factoryOperator.id, plantId: factoryOperator.plantId, issuedAt: now }
    const token = reusable ? currentToken : createQrToken(payload, signingKey)
    return json(response, 200, { token, surveyUrl: `${publicBaseUrl(request)}/s/${encodeURIComponent(token)}`, expiresAt: new Date(payload.issuedAt + qrLifetimeMs).toISOString(), operatorName: factoryOperator.displayName, plantName: factoryOperator.plantName })
  }

  if (path.startsWith('survey/')) {
    const currentSettings = { ...settings, ...(directoryStore ? await directoryStore.getSettings() : {}) }
    const repeatWindowMs = Number(currentSettings.repeatWindowDays) * 86400000
    const nextAllowed = submittedAt => nextAllowedAt(submittedAt, repeatWindowMs)
    const token = decodeURIComponent(parseGuidPath(path, 'survey/'))
    // Accept already-issued JSON links while new links use compact signed bytes.
    const payload = token.startsWith('q1_') ? readQrToken(token, signingKey) : verifyWithKey(token, signingKey)
    if (!payload) return json(response, 400, { code: 'QR_INVALID', message: 'QR კოდი არასწორია.' })
    if (token.startsWith('q1_')) {
      payload.expiresAt = payload.issuedAt + qrLifetimeMs
      payload.surveyExpiresAt = payload.issuedAt + surveyLifetimeMs
      const factoryOperator = directoryStore ? await directoryStore.findOperator(payload.operatorId) : operators.find(item => item.id === payload.operatorId)
      const plant = directoryStore ? await directoryStore.findPlant(payload.plantId) : plants.find(item => item.id === payload.plantId)
      if (!factoryOperator || !plant) return json(response, 400, { code: 'QR_INVALID', message: 'QR კოდი აღარ არის მოქმედი.' })
      payload.operatorName = factoryOperator.displayName
      payload.plantName = plant.name
    }
    if (method === 'GET') {
      if (payload.expiresAt <= Date.now()) return json(response, 400, { code: 'QR_EXPIRED', message: 'QR კოდს ვადა გაუვიდა. გთხოვთ თავიდან დაასკანეროთ.' })
      const hash = deviceHash(getOrSetDeviceToken(request, response))
      const cutoff = new Date(Date.now() - repeatWindowMs).toISOString()
      const recent = repeatWindowMs === 0 ? null : directoryStore
        ? await directoryStore.findRecentResponse(hash, cutoff)
        : deviceResponses.filter(item => item.deviceTokenHash === hash && new Date(item.submittedAt) > new Date(cutoff)).sort((a, b) => b.submittedAt.localeCompare(a.submittedAt))[0]
      return json(response, 200, { sessionId: payload.sessionId, plant: payload.plantName, operatorName: payload.operatorName, surveyExpiresAt: new Date(payload.surveyExpiresAt).toISOString(), blocked: Boolean(recent), nextAllowedAt: recent ? nextAllowed(recent.submittedAt) : null, privacyNotice: currentSettings.privacyNotice })
    }
    if (method === 'POST') {
      if (payload.surveyExpiresAt <= Date.now()) return json(response, 400, { code: 'SURVEY_EXPIRED', message: 'შეფასების სესიას ვადა გაუვიდა.' })
      if (!Array.isArray(input.ratings) || input.ratings.length !== 3 || new Set(input.ratings.map(item => item.serviceType)).size !== 3 || input.ratings.some(item => !serviceTypes.includes(item.serviceType) || item.score < 1 || item.score > 5)) return json(response, 400, { message: 'სამივე შეფასება სავალდებულოა.' })
      const carPlate = normalizeCarPlate(input.carPlate)
      if (!carPlate) return json(response, 400, { code: 'CAR_PLATE_INVALID', message: 'ნომერი უნდა იყოს ფორმატით AA123AA ან AA-123-AA.' })
      const idempotencyKey = String(input.idempotencyKey || '').trim()
      if (!idempotencyKey || idempotencyKey.length > 100) return json(response, 400, { message: 'შეფასების მოთხოვნა არასწორია.' })
      const duplicate = directoryStore ? await directoryStore.findResponseByIdempotencyKey(idempotencyKey) : (idempotencyKeys.has(idempotencyKey) ? idempotencyKeys.get(idempotencyKey) : null)
      if (duplicate) return json(response, 200, { responseId: duplicate.id, duplicate: true, nextAllowedAt: nextAllowed(duplicate.submittedAt) })
      const hash = deviceHash(getOrSetDeviceToken(request, response))
      const cutoff = new Date(Date.now() - repeatWindowMs).toISOString()
      const recent = repeatWindowMs === 0 ? null : directoryStore
        ? await directoryStore.findRecentResponse(hash, cutoff)
        : deviceResponses.filter(item => item.deviceTokenHash === hash && new Date(item.submittedAt) > new Date(cutoff)).sort((a, b) => b.submittedAt.localeCompare(a.submittedAt))[0]
      if (recent) return json(response, 429, { code: 'REPEAT_BLOCKED', message: 'შეფასება უკვე დაფიქსირებულია.', nextAllowedAt: nextAllowed(recent.submittedAt) })
      if (!directoryStore && completedSessions.has(payload.sessionId)) return json(response, 409, { code: 'SESSION_COMPLETED', message: 'ამ QR კოდით შეფასება უკვე დაფიქსირებულია.' })
      const responseId = crypto.randomUUID()
      const submittedAt = new Date().toISOString()
      const cleanRatings = input.ratings.map(item => ({ id: crypto.randomUUID(), serviceType: item.serviceType, score: Number(item.score), comment: String(item.comment || '').replace(/<[^>]+>/g, '').slice(0, 1000) }))
      if (directoryStore) {
        const created = await directoryStore.createResponse({ id: responseId, sessionId: payload.sessionId, deviceTokenHash: hash, submittedAt, plantId: payload.plantId, operatorId: payload.operatorId, idempotencyKey, carPlate }, cleanRatings, cutoff)
        if (!created) {
          const latest = await directoryStore.findRecentResponse(hash, cutoff)
          if (latest) return json(response, 429, { code: 'REPEAT_BLOCKED', message: 'შეფასება უკვე დაფიქსირებულია.', nextAllowedAt: nextAllowed(latest.submittedAt) })
          return json(response, 409, { code: 'SESSION_COMPLETED', message: 'ამ QR კოდით შეფასება უკვე დაფიქსირებულია.' })
        }
      } else {
        for (const item of cleanRatings) ratings.push({ ...item, responseId, submittedAt, carPlate, plantId: payload.plantId, plant: payload.plantName, operatorId: payload.operatorId, operator: payload.operatorName })
        deviceResponses.push({ responseId, deviceTokenHash: hash, submittedAt })
        idempotencyKeys.set(idempotencyKey, { id: responseId, submittedAt })
        completedSessions.add(payload.sessionId)
      }
      return json(response, 200, { responseId, duplicate: false, nextAllowedAt: nextAllowed(submittedAt), message: 'გმადლობთ შეფასებისთვის!' })
    }
  }

  if (path === 'admin/dashboard' && method === 'GET') {
    const allRatings = directoryStore ? normalizedRatings(await directoryStore.listRatings()) : ratings
    return json(response, 200, dashboard(allRatings, request.query || {}))
  }
  if (path === 'admin/responses' && method === 'GET') {
    const allRatings = directoryStore ? normalizedRatings(await directoryStore.listRatings()) : ratings
    const items = responseRows(allRatings, request.query || {})
    const page = Math.max(1, Number(request.query.page || 1))
    const pageSize = Math.min(200, Math.max(1, Number(request.query.pageSize || 50)))
    return json(response, 200, { total: items.length, page, pageSize, items: items.sort((a, b) => b.submittedAt.localeCompare(a.submittedAt)).slice((page - 1) * pageSize, page * pageSize) })
  }
  if (path.startsWith('admin/responses/') && method === 'DELETE') {
    const id = parseGuidPath(path, 'admin/responses/')
    const exists = directoryStore
      ? await directoryStore.deleteResponse(id)
      : ratings.some(item => item.responseId === id)
    if (!exists) return json(response, 404, { message: 'შეფასება ვერ მოიძებნა.' })
    if (!directoryStore) ratings = ratings.filter(item => item.responseId !== id)
    audit('Delete', 'SurveyResponse', id)
    return noContent(response)
  }
  if (path.startsWith('admin/exports/') && method === 'GET') {
    const format = parseGuidPath(path, 'admin/exports/').toLowerCase()
    const allRatings = directoryStore ? normalizedRatings(await directoryStore.listRatings()) : ratings
    const content = exportCsv(responseRows(allRatings, request.query || {}))
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
      if (hasOperators) return json(response, 409, { message: 'ქარხანა დაკავშირებულია ოპერატორთან და ვერ წაიშლება.' })
      if (directoryStore) await directoryStore.deactivatePlant(id)
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
      if (directoryStore) await directoryStore.deactivateOperator(id)
      else operators = operators.filter(item => item.id !== id)
      audit('Delete', 'Operator', id); return noContent(response)
    }
  }
  if (path === 'admin/settings' && method === 'GET') return json(response, 200, { ...settings, ...(directoryStore ? await directoryStore.getSettings() : {}) })
  if (path === 'admin/settings' && method === 'PUT') {
    if (Object.hasOwn(input, 'repeatWindowDays') && (!/^(0|[1-9]\d{0,3})$/.test(String(input.repeatWindowDays)) || Number(input.repeatWindowDays) > 3650)) {
      return json(response, 400, { message: 'მიუთითეთ დღეების მთელი რაოდენობა 0-დან 3650-მდე.' })
    }
    const values = Object.fromEntries(Object.entries(input).map(([key, value]) => [key, String(value)]))
    if (directoryStore) await directoryStore.saveSettings(values)
    else settings = { ...settings, ...values }
    audit('Update', 'ApplicationSettings', 'global')
    return noContent(response)
  }
  if (path === 'admin/audit' && method === 'GET') return json(response, 200, audits.slice(0, Math.min(500, Math.max(1, Number(request.query.take || 100)))))

  return json(response, 404, { message: `Route not found: ${method} /${path}` })
}
