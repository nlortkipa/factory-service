import crypto from 'node:crypto'

const prefix = 'q1_'
const bodyLength = 56 // Three UUIDs and an issuance timestamp in milliseconds.

export function createQrToken(payload, key) {
  const body = Buffer.alloc(bodyLength)
  for (const [index, name] of ['sessionId', 'operatorId', 'plantId'].entries()) {
    Buffer.from(payload[name].replaceAll('-', ''), 'hex').copy(body, index * 16)
  }
  body.writeBigInt64BE(BigInt(payload.issuedAt), 48)
  const signature = crypto.createHmac('sha256', key).update(prefix).update(body).digest()
  return prefix + Buffer.concat([body, signature]).toString('base64url')
}

export function readQrToken(token, key) {
  if (!/^q1_[A-Za-z0-9_-]{118}$/.test(token)) return null
  const bytes = Buffer.from(token.slice(prefix.length), 'base64url')
  if (bytes.length !== bodyLength + 32 || bytes.toString('base64url') !== token.slice(prefix.length)) return null
  const body = bytes.subarray(0, bodyLength)
  const expected = crypto.createHmac('sha256', key).update(prefix).update(body).digest()
  if (!crypto.timingSafeEqual(bytes.subarray(bodyLength), expected)) return null
  const payload = {}
  for (const [index, name] of ['sessionId', 'operatorId', 'plantId'].entries()) {
    const hex = body.subarray(index * 16, (index + 1) * 16).toString('hex')
    payload[name] = `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
  }
  payload.issuedAt = Number(body.readBigInt64BE(48))
  if (!Number.isSafeInteger(payload.issuedAt) || payload.issuedAt < 0) return null
  return payload
}
