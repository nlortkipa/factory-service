import { neon } from '@neondatabase/serverless'

async function initialize(sql) {
  await sql`
    CREATE TABLE IF NOT EXISTS factory_service_plants (
      id uuid PRIMARY KEY,
      name text NOT NULL,
      address text NOT NULL DEFAULT '',
      time_zone text NOT NULL DEFAULT 'Asia/Tbilisi'
    )
  `
  await sql`
    CREATE TABLE IF NOT EXISTS factory_service_operators (
      id uuid PRIMARY KEY,
      operator_id text NOT NULL UNIQUE,
      display_name text NOT NULL,
      plant_id uuid NOT NULL REFERENCES factory_service_plants(id) ON DELETE RESTRICT
    )
  `
  await sql`
    CREATE TABLE IF NOT EXISTS factory_service_survey_responses (
      id uuid PRIMARY KEY,
      session_id uuid NOT NULL UNIQUE,
      device_token_hash text NOT NULL,
      submitted_at timestamptz NOT NULL,
      plant_id uuid NOT NULL REFERENCES factory_service_plants(id) ON DELETE RESTRICT,
      operator_id uuid NOT NULL REFERENCES factory_service_operators(id) ON DELETE RESTRICT,
      idempotency_key text NOT NULL UNIQUE
    )
  `
  await sql`
    CREATE INDEX IF NOT EXISTS factory_service_survey_responses_device_submitted_idx
    ON factory_service_survey_responses (device_token_hash, submitted_at DESC)
  `
  await sql`
    CREATE TABLE IF NOT EXISTS factory_service_ratings (
      id uuid PRIMARY KEY,
      response_id uuid NOT NULL REFERENCES factory_service_survey_responses(id) ON DELETE CASCADE,
      service_type text NOT NULL CHECK (service_type IN ('Sales', 'Scale', 'Loading')),
      score integer NOT NULL CHECK (score BETWEEN 1 AND 5),
      comment text NOT NULL DEFAULT '',
      UNIQUE (response_id, service_type)
    )
  `
  await sql`
    CREATE TABLE IF NOT EXISTS factory_service_mfa_challenges (
      nonce text PRIMARY KEY,
      code_mac text NOT NULL,
      expires_at timestamptz NOT NULL,
      attempts integer NOT NULL DEFAULT 0,
      used boolean NOT NULL DEFAULT false
    )
  `
}

class DirectoryStore {
  constructor(connectionString) {
    this.sql = neon(connectionString)
    this.ready = initialize(this.sql)
  }

  async createMfaChallenge(entity) {
    await this.ready
    await this.sql`
      DELETE FROM factory_service_mfa_challenges WHERE expires_at <= now()
    `
    await this.sql`
      INSERT INTO factory_service_mfa_challenges (nonce, code_mac, expires_at)
      VALUES (${entity.nonce}, ${entity.codeMac}, ${entity.expiresAt})
    `
  }

  async consumeMfaChallenge(nonce, codeMac) {
    await this.ready
    const rows = await this.sql`
      UPDATE factory_service_mfa_challenges
      SET attempts = attempts + 1,
          used = CASE WHEN code_mac = ${codeMac} THEN true ELSE used END
      WHERE nonce = ${nonce}
        AND expires_at > now()
        AND attempts < 5
        AND used = false
      RETURNING code_mac = ${codeMac} AS valid, 5 - attempts AS "attemptsRemaining"
    `
    return rows[0] || null
  }

  async listPlants() {
    await this.ready
    return this.sql`
      SELECT id::text AS id, name, address, time_zone AS "timeZone"
      FROM factory_service_plants
      ORDER BY name, id
    `
  }

  async findPlant(id) {
    await this.ready
    const rows = await this.sql`
      SELECT id::text AS id, name, address, time_zone AS "timeZone"
      FROM factory_service_plants
      WHERE id = ${id}
    `
    return rows[0] || null
  }

  async createPlant(entity) {
    await this.ready
    const rows = await this.sql`
      INSERT INTO factory_service_plants (id, name, address, time_zone)
      VALUES (${entity.id}, ${entity.name}, ${entity.address}, ${entity.timeZone})
      RETURNING id::text AS id, name, address, time_zone AS "timeZone"
    `
    return rows[0]
  }

  async updatePlant(entity) {
    await this.ready
    const rows = await this.sql`
      UPDATE factory_service_plants
      SET name = ${entity.name}, address = ${entity.address}, time_zone = ${entity.timeZone}
      WHERE id = ${entity.id}
      RETURNING id::text AS id, name, address, time_zone AS "timeZone"
    `
    return rows[0] || null
  }

  async plantHasOperators(id) {
    await this.ready
    const rows = await this.sql`
      SELECT EXISTS(
        SELECT 1 FROM factory_service_operators WHERE plant_id = ${id}
      ) AS value
    `
    return rows[0]?.value === true
  }

  async deletePlant(id) {
    await this.ready
    const rows = await this.sql`
      DELETE FROM factory_service_plants WHERE id = ${id} RETURNING id::text AS id
    `
    return rows.length > 0
  }

  async listOperators() {
    await this.ready
    return this.sql`
      SELECT o.id::text AS id, o.operator_id AS "operatorId", o.display_name AS "displayName",
             o.plant_id::text AS "plantId", p.name AS "plantName"
      FROM factory_service_operators o
      JOIN factory_service_plants p ON p.id = o.plant_id
      ORDER BY o.display_name, o.id
    `
  }

  async findOperator(id) {
    await this.ready
    const rows = await this.sql`
      SELECT o.id::text AS id, o.operator_id AS "operatorId", o.display_name AS "displayName",
             o.plant_id::text AS "plantId", p.name AS "plantName"
      FROM factory_service_operators o
      JOIN factory_service_plants p ON p.id = o.plant_id
      WHERE o.id = ${id}
    `
    return rows[0] || null
  }

  async findOperatorByOperatorId(operatorId) {
    await this.ready
    const rows = await this.sql`
      SELECT o.id::text AS id, o.operator_id AS "operatorId", o.display_name AS "displayName",
             o.plant_id::text AS "plantId", p.name AS "plantName"
      FROM factory_service_operators o
      JOIN factory_service_plants p ON p.id = o.plant_id
      WHERE o.operator_id = ${operatorId}
    `
    return rows[0] || null
  }

  async createOperator(entity) {
    await this.ready
    const rows = await this.sql`
      INSERT INTO factory_service_operators (id, operator_id, display_name, plant_id)
      VALUES (${entity.id}, ${entity.operatorId}, ${entity.displayName}, ${entity.plantId})
      RETURNING id::text AS id
    `
    return this.findOperator(rows[0].id)
  }

  async updateOperator(entity) {
    await this.ready
    const rows = await this.sql`
      UPDATE factory_service_operators
      SET display_name = ${entity.displayName}, plant_id = ${entity.plantId}
      WHERE id = ${entity.id}
      RETURNING id::text AS id
    `
    return rows.length ? this.findOperator(rows[0].id) : null
  }

  async deleteOperator(id) {
    await this.ready
    const rows = await this.sql`
      DELETE FROM factory_service_operators WHERE id = ${id} RETURNING id::text AS id
    `
    return rows.length > 0
  }

  async operatorHasRatings(id) {
    await this.ready
    const rows = await this.sql`
      SELECT EXISTS(
        SELECT 1 FROM factory_service_survey_responses WHERE operator_id = ${id}
      ) AS value
    `
    return rows[0]?.value === true
  }

  async findRecentResponse(deviceTokenHash, cutoff) {
    await this.ready
    const rows = await this.sql`
      SELECT id::text AS id, submitted_at AS "submittedAt"
      FROM factory_service_survey_responses
      WHERE device_token_hash = ${deviceTokenHash} AND submitted_at >= ${cutoff}
      ORDER BY submitted_at DESC
      LIMIT 1
    `
    return rows[0] || null
  }

  async findResponseByIdempotencyKey(idempotencyKey) {
    await this.ready
    const rows = await this.sql`
      SELECT id::text AS id, submitted_at AS "submittedAt"
      FROM factory_service_survey_responses
      WHERE idempotency_key = ${idempotencyKey}
      LIMIT 1
    `
    return rows[0] || null
  }

  async createResponse(entity, ratings, cutoff) {
    await this.ready
    const rows = await this.sql`
      INSERT INTO factory_service_survey_responses
        (id, session_id, device_token_hash, submitted_at, plant_id, operator_id, idempotency_key)
      SELECT ${entity.id}, ${entity.sessionId}, ${entity.deviceTokenHash}, ${entity.submittedAt},
             ${entity.plantId}, ${entity.operatorId}, ${entity.idempotencyKey}
      WHERE NOT EXISTS (
        SELECT 1 FROM factory_service_survey_responses
        WHERE device_token_hash = ${entity.deviceTokenHash} AND submitted_at >= ${cutoff}
      )
      ON CONFLICT DO NOTHING
      RETURNING id::text AS id, submitted_at AS "submittedAt"
    `
    if (!rows.length) return null

    for (const rating of ratings) {
      await this.sql`
        INSERT INTO factory_service_ratings (id, response_id, service_type, score, comment)
        VALUES (${rating.id}, ${entity.id}, ${rating.serviceType}, ${rating.score}, ${rating.comment})
        ON CONFLICT (response_id, service_type) DO NOTHING
      `
    }
    return rows[0]
  }

  async listRatings() {
    await this.ready
    return this.sql`
      SELECT r.id::text AS id, sr.id::text AS "responseId", sr.submitted_at AS "submittedAt",
             sr.plant_id::text AS "plantId", p.name AS plant,
             sr.operator_id::text AS "operatorId", o.display_name AS operator,
             r.service_type AS "serviceType", r.score, r.comment
      FROM factory_service_ratings r
      JOIN factory_service_survey_responses sr ON sr.id = r.response_id
      JOIN factory_service_plants p ON p.id = sr.plant_id
      JOIN factory_service_operators o ON o.id = sr.operator_id
      ORDER BY sr.submitted_at DESC, r.service_type
    `
  }

  async deleteResponse(id) {
    await this.ready
    const rows = await this.sql`
      DELETE FROM factory_service_survey_responses
      WHERE id = ${id}
      RETURNING id::text AS id
    `
    return rows.length > 0
  }
}

let store

export function getDirectoryStore() {
  const connectionString = process.env.DATABASE_URL || process.env.POSTGRES_URL
  if (!connectionString) return null
  if (!store) store = new DirectoryStore(connectionString)
  return store
}
