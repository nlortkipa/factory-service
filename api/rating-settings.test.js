import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import { test } from 'node:test'

delete process.env.DATABASE_URL
delete process.env.POSTGRES_URL
delete process.env.VERCEL
process.env.AUTH_SIGNING_KEY = 'settings-test-auth-key'
process.env.QR_SIGNING_KEY = 'settings-test-qr-key'
const { default: handler } = await import('./index.js')
const encoded = Buffer.from(JSON.stringify({ type: 'admin-session', username: process.env.ADMIN_USERNAME || 'admin', expiresAt: Date.now() + 864000000 })).toString('base64url')
const authorization = `Bearer ${encoded}.${crypto.createHmac('sha256', process.env.AUTH_SIGNING_KEY).update(encoded).digest('base64url')}`
const operatorId = '07IdLcVyn4q-s77Or8GldZwg_BsoQzSM'
const input = () => ({ idempotencyKey: crypto.randomUUID(), ratings: ['Sales', 'Scale', 'Loading'].map(serviceType => ({ serviceType, score: 4 })) })
async function request(path, method = 'GET', body = {}, headers = {}, query = {}) {
  const result = { headers: {} }
  await handler({ url: `/api/${path}`, method, body, headers: { host: 'factory.test', ...headers }, query }, {
    status(value) { result.status = value; return this },
    setHeader(name, value) { result.headers[name] = value; return this },
    end(value) { result.body = value ? JSON.parse(value) : null; return this },
  })
  return result
}
const admin = (path, method, body, query) => request(path, method, body, { authorization }, query)
const configure = days => admin('admin/settings', 'PUT', { repeatWindowDays: days })
const qrPath = async () => `survey/${(await request('agent/qr', 'POST', { operatorId })).body.token}`

test('settings require authentication and reject invalid days without changing the saved value', async () => {
  assert.equal((await request('admin/settings')).status, 401)
  assert.equal((await request('admin/settings', 'PUT', { repeatWindowDays: '0' })).status, 401)
  assert.equal((await configure('7')).status, 204)
  for (const value of ['', '-1', '1.5', 'abc', null, true, '3651', 'Infinity']) {
    assert.equal((await configure(value)).status, 400, String(value))
    assert.equal((await admin('admin/settings')).body.repeatWindowDays, '7')
  }
  assert.equal((await configure(3650)).status, 204)
  assert.equal((await admin('admin/settings')).body.repeatWindowDays, '3650')
  await configure('7')
})

test('saved cooldown controls GET, direct POST, changed settings, exact expiry, and idempotent retries', async t => {
  const now = Date.now()
  t.mock.timers.enable({ apis: ['Date'], now })
  try {
    await configure('3')
    const firstPath = await qrPath()
    const opened = await request(firstPath)
    const headers = { cookie: opened.headers['Set-Cookie'].split(';')[0] }
    const submission = input()
    const first = await request(firstPath, 'POST', submission, headers)
    assert.equal(first.status, 200)
    const expected = new Date(now + 3 * 86400000).toISOString()
    assert.equal(first.body.nextAllowedAt, expected)
    t.mock.timers.tick(1000)
    const retry = await request(firstPath, 'POST', submission, headers)
    assert.equal(retry.body.duplicate, true)
    assert.equal(retry.body.nextAllowedAt, expected)
    const nextPath = await qrPath()
    assert.equal((await request(nextPath, 'GET', {}, headers)).body.blocked, true)
    assert.equal((await request(nextPath, 'POST', input(), headers)).status, 429)
    await configure('0')
    assert.equal((await request(nextPath, 'GET', {}, headers)).body.blocked, false)
    assert.equal((await request(nextPath, 'POST', input(), headers)).status, 200)
    await configure('2')
    t.mock.timers.tick(2 * 86400000 - 1)
    const boundaryPath = await qrPath()
    assert.equal((await request(boundaryPath, 'GET', {}, headers)).body.blocked, true)
    assert.equal((await request(boundaryPath, 'POST', input(), headers)).status, 429)
    t.mock.timers.tick(1)
    assert.equal((await request(boundaryPath, 'GET', {}, headers)).body.blocked, false)
    assert.equal((await request(boundaryPath, 'POST', input(), headers)).status, 200)
  } finally { t.mock.timers.reset(); await configure('7') }
})

test('QR stays valid for five minutes and expires at the boundary', async () => {
  const realNow = Date.now
  let now = realNow()
  Date.now = () => now
  try {
    const qr = await request('agent/qr', 'POST', { operatorId })
    assert.equal(new Date(qr.body.expiresAt).getTime() - now, 300000)
    now += 299999
    assert.equal((await request(`survey/${qr.body.token}`)).status, 200)
    now += 1
    assert.equal((await request(`survey/${qr.body.token}`)).body.code, 'QR_EXPIRED')
  } finally { Date.now = realNow }
})

test('overview operator filter keeps all service metrics and combines with plant filter', async () => {
  const operators = (await admin('admin/operators')).body
  const selected = operators[0]
  const dashboard = (await admin('admin/dashboard', 'GET', {}, { operatorId: selected.id })).body
  assert.ok(dashboard.total > 0)
  for (const type of ['sales', 'scale', 'loading']) assert.equal(dashboard[type].count, dashboard.total)
  const other = (await admin('admin/operators', 'POST', { displayName: 'Filter regression', plantId: selected.plantId })).body
  assert.equal((await admin('admin/dashboard', 'GET', {}, { operatorId: other.id })).body.total, 0)
  assert.equal((await admin('admin/dashboard', 'GET', {}, { operatorId: selected.id, plantId: crypto.randomUUID() })).body.total, 0)
})

test('admin can delete an operator with ratings without removing historical results', async () => {
  const selected = (await admin('admin/operators')).body.find(item => item.operatorId === operatorId)
  assert.ok(selected)
  const before = (await admin('admin/dashboard', 'GET', {}, { operatorId: selected.id })).body.total
  assert.ok(before > 0)

  assert.equal((await admin(`admin/operators/${selected.id}`, 'DELETE')).status, 204)
  assert.equal((await admin('admin/operators')).body.some(item => item.id === selected.id), false)
  assert.equal((await request('agent/qr', 'POST', { operatorId })).status, 404)
  assert.equal((await admin('admin/dashboard', 'GET', {}, { operatorId: selected.id })).body.total, before)
})

test('admin can delete a plant after deleting all of its operators', async () => {
  const plant = (await admin('admin/plants', 'POST', { name: 'Delete regression', address: 'Test' })).body
  const operator = (await admin('admin/operators', 'POST', { displayName: 'Delete regression', plantId: plant.id })).body

  assert.equal((await admin(`admin/plants/${plant.id}`, 'DELETE')).status, 409)
  assert.equal((await admin(`admin/operators/${operator.id}`, 'DELETE')).status, 204)
  assert.equal((await admin(`admin/plants/${plant.id}`, 'DELETE')).status, 204)
  assert.equal((await admin('admin/plants')).body.some(item => item.id === plant.id), false)
})
