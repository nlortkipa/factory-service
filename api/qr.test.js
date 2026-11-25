import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import { test } from 'node:test'
import { createQrToken, readQrToken } from './qr-token.js'

// These integration tests must only use the resettable in-memory directory.
delete process.env.DATABASE_URL
delete process.env.POSTGRES_URL
delete process.env.VERCEL
process.env.QR_SIGNING_KEY = 'qr-regression-test-key'
const { default: handler } = await import('./index.js')
const key = process.env.QR_SIGNING_KEY
const operatorId = '07IdLcVyn4q-s77Or8GldZwg_BsoQzSM'
const validPayload = () => ({ sessionId: crypto.randomUUID(), operatorId: 'a2d623d3-87ac-4208-8cfa-39892988944c', plantId: '78f731db-9089-4b2e-87a3-3fc1b24f4685', issuedAt: Date.now() })

async function request(path, method = 'GET', body = {}, headers = {}) {
  const result = { headers: {} }
  const response = {
    status(value) { result.status = value; return this },
    setHeader(name, value) { result.headers[name] = value; return this },
    end(value) { result.body = value ? JSON.parse(value) : null; return this },
  }
  await handler({ url: `/api/${path}`, method, body, headers: { host: 'factory.example', ...headers }, query: {} }, response)
  return result
}

test('compact QR tokens preserve context, stay short, and reject tampering', () => {
  const payload = validPayload()
  const token = createQrToken(payload, key)
  assert.equal(token.length, 121)
  assert.deepEqual(readQrToken(token, key), payload)
  assert.equal(readQrToken(token, 'wrong-key'), null)
  for (let index = 3; index < token.length; index++) {
    const changed = token.slice(0, index) + (token[index] === 'A' ? 'B' : 'A') + token.slice(index + 1)
    assert.equal(readQrToken(changed, key), null)
  }
  for (const invalid of ['', 'q1_!', token + 'A', token.slice(0, -1)]) assert.equal(readQrToken(invalid, key), null)
})

test('invalid and missing operator IDs return specific Georgian errors', async () => {
  const missing = await request('agent/qr', 'POST')
  assert.equal(missing.status, 400)
  assert.equal(missing.body.code, 'INVALID_OPERATOR_ID')
  const unknown = await request('agent/qr', 'POST', { operatorId: 'invalid' })
  assert.equal(unknown.status, 404)
  assert.equal(unknown.body.message, 'ოპერატორი ვერ მოიძებნა.')
})

test('short links open with names, submit all scores, and prevent duplicate sessions', async () => {
  const qr = await request('agent/qr', 'POST', { operatorId })
  assert.equal(qr.status, 200)
  assert.ok(qr.body.surveyUrl.length < 170)
  const path = `survey/${qr.body.token}`
  const opened = await request(path)
  assert.equal(opened.status, 200)
  assert.equal(opened.body.operatorName, qr.body.operatorName)
  assert.equal(opened.body.plant, qr.body.plantName)
  const input = { idempotencyKey: crypto.randomUUID(), carPlate: 'aa-123-bb', ratings: ['Sales', 'Scale', 'Loading'].map(serviceType => ({ serviceType, score: 5 })) }
  assert.equal((await request(path, 'POST', input)).status, 200)
  assert.equal((await request(path, 'POST', input)).body.duplicate, true)
  assert.equal((await request(path, 'POST', { ...input, idempotencyKey: crypto.randomUUID() })).status, 409)
})

test('polling keeps unused QR stable and replaces it after a successful rating', async () => {
  const first = await request('agent/qr', 'POST', { operatorId })
  const poll = () => request('agent/qr', 'POST', { operatorId, currentToken: first.body.token })
  assert.deepEqual((await poll()).body, first.body)
  await request(`survey/${first.body.token}`)
  assert.equal((await poll()).body.token, first.body.token, 'Scanning alone must not rotate the code')
  assert.equal((await request(`survey/${first.body.token}`, 'POST', { ratings: [] })).status, 400)
  assert.equal((await poll()).body.token, first.body.token, 'Rejected submissions must not rotate the code')
  const input = { idempotencyKey: crypto.randomUUID(), carPlate: 'aa-123-bb', ratings: ['Sales', 'Scale', 'Loading'].map(serviceType => ({ serviceType, score: 5 })) }
  assert.equal((await request(`survey/${first.body.token}`, 'POST', input)).status, 200)
  const next = await poll()
  assert.equal(next.status, 200)
  assert.notEqual(next.body.token, first.body.token)
  assert.notEqual(readQrToken(next.body.token, key).sessionId, readQrToken(first.body.token, key).sessionId)
  assert.equal((await request('agent/qr', 'POST', { operatorId, currentToken: next.body.token })).body.token, next.body.token)
  assert.equal((await request(`survey/${next.body.token}`, 'POST', { ...input, idempotencyKey: crypto.randomUUID() })).status, 200)
})

test('expired, invalid and mismatched current tokens are replaced', async () => {
  const first = await request('agent/qr', 'POST', { operatorId })
  const payload = readQrToken(first.body.token, key)
  for (const currentToken of [
    'invalid',
    createQrToken({ ...payload, issuedAt: Date.now() - 301_000 }, key),
    createQrToken({ ...payload, operatorId: crypto.randomUUID() }, key),
    createQrToken({ ...payload, plantId: crypto.randomUUID() }, key),
  ]) {
    const next = await request('agent/qr', 'POST', { operatorId, currentToken })
    assert.equal(next.status, 200)
    assert.notEqual(next.body.token, currentToken)
    assert.equal(readQrToken(next.body.token, key).operatorId, payload.operatorId)
  }
})

test('scan expiry and submission expiry are still separate', async () => {
  const token = createQrToken({ ...validPayload(), issuedAt: Date.now() - 301_000 }, key)
  assert.equal((await request(`survey/${token}`)).body.code, 'QR_EXPIRED')
  const input = { idempotencyKey: crypto.randomUUID(), carPlate: 'aa-123-bb', ratings: ['Sales', 'Scale', 'Loading'].map(serviceType => ({ serviceType, score: 4 })) }
  assert.equal((await request(`survey/${token}`, 'POST', input)).status, 200)
  const expired = createQrToken({ ...validPayload(), issuedAt: Date.now() - 21 * 60_000 }, key)
  assert.equal((await request(`survey/${expired}`, 'POST', input)).body.code, 'SURVEY_EXPIRED')
})

test('already-issued signed JSON links continue to open', async () => {
  const payload = { ...validPayload(), operatorName: 'ტესტ ოპერატორი', plantName: 'ტესტ ქარხანა', expiresAt: Date.now() + 60_000, surveyExpiresAt: Date.now() + 1_200_000 }
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url')
  const token = `${body}.${crypto.createHmac('sha256', key).update(body).digest('base64url')}`
  const opened = await request(`survey/${token}`)
  assert.equal(opened.status, 200)
  assert.equal(opened.body.plant, payload.plantName)
})

test('car plate is required and normalized before ratings are saved', async () => {
  const { normalizeCarPlate } = await import('./index.js')
  for (const entered of ['aa123aa', 'AA123AA', 'AA-123-AA', 'aa-123aa', 'AA123-AA', ' Aa-123-aA ']) assert.equal(normalizeCarPlate(entered), 'AA123AA')
  for (const invalid of [undefined, '', 'AA 123 AA', 'AA--123AA', 'AA.123.AA', 'A123AA', 'AAA123AA', 'AA12AA', 'AA1234AA', 'AA123A', '12ABC34', 'ააა123აა', 'АА123АА', 'AA–123–AA']) assert.equal(normalizeCarPlate(invalid), null)

  const qr = await request('agent/qr', 'POST', { operatorId })
  const path = `survey/${qr.body.token}`
  const ratings = ['Sales', 'Scale', 'Loading'].map(serviceType => ({ serviceType, score: 3 }))
  for (const carPlate of [undefined, '', '<b>', 'AB 123 CD', 'AB1234CD']) {
    const rejected = await request(path, 'POST', { idempotencyKey: crypto.randomUUID(), carPlate, ratings })
    assert.equal(rejected.status, 400)
    assert.equal(rejected.body.code, 'CAR_PLATE_INVALID')
  }
  const created = await request(path, 'POST', { idempotencyKey: crypto.randomUUID(), carPlate: 'tb-777-zz', ratings })
  assert.equal(created.status, 200)
})
