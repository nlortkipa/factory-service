import React, { useEffect, useId, useMemo, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { api } from './api'
import './styles.css'
import './editor.css'

const serviceCopy = {
  Sales: { title: 'გაყიდვების ოპერატორი', subtitle: 'კონკრეტული ოპერატორის შეფასება' },
  Scale: { title: 'სასწორის მომსახურება', subtitle: 'ქარხნის მიხედვით' },
  Loading: { title: 'დატვირთვის მომსახურება', subtitle: 'ქარხნის მიხედვით' },
}
const scoreLabels = ['ძალიან ცუდი', 'ცუდი', 'საშუალო', 'კარგი', 'შესანიშნავი']
const pageCopy = {
  dashboard: 'შედეგები და ტენდენციები ერთ ხედში',
  responses: 'თითოეული შეფასების დეტალური ისტორია',
  plants: 'ქარხნების მისამართები და მონაცემები',
  operators: 'ოპერატორები და პერსონალური კლიენტები',
  settings: 'შეფასების სისტემის საერთო წესები',
}
const adminBypass = import.meta.env.VITE_ADMIN_BYPASS === 'true'

function createIdempotencyKey() {
  const cryptoApi = globalThis.crypto
  if (typeof cryptoApi?.randomUUID === 'function') return cryptoApi.randomUUID()

  const bytes = new Uint8Array(16)
  if (typeof cryptoApi?.getRandomValues === 'function') cryptoApi.getRandomValues(bytes)
  else for (let index = 0; index < bytes.length; index += 1) bytes[index] = Math.floor(Math.random() * 256)

  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

function App() {
  const path = window.location.pathname
  if (path.startsWith('/s/')) return <SurveyPage token={decodeURIComponent(path.slice(3))} />
  if (path.startsWith('/admin')) return <AdminApp />
  return <Landing />
}

function Landing() {
  return <main className="landing">
    <header className="landing-nav"><Brand/><a href="/admin">ადმინისტრაციული პორტალი <span aria-hidden="true">→</span></a></header>
    <section className="landing-hero">
      <div className="landing-copy">
        <p className="eyebrow">მომსახურების შეფასება</p>
        <h1>მომსახურება, რომელსაც <em>უკუკავშირი</em> აუმჯობესებს.</h1>
        <p>დაასკანერეთ ოპერატორის ეკრანზე ნაჩვენები QR კოდი და შეაფასეთ გამოცდილება რამდენიმე წამში.</p>
        <div className="landing-actions"><span className="landing-step"><b>01</b> დაასკანერეთ კოდი</span><span className="landing-step"><b>02</b> შეაფასეთ მომსახურება</span></div>
      </div>
      <div className="landing-signal" aria-hidden="true"><span>QR</span><i/><i/><i/><p>SCAN · RATE · IMPROVE</p></div>
    </section>
    <footer className="landing-footer"><span>ქართული ცემენტი</span><span>უსაფრთხო და ანონიმური უკუკავშირი</span></footer>
  </main>
}

function Brand() { return <div className="brand"><span>KC</span><strong>KARTULI<br/>CEMENT</strong></div> }

function NavIcon({ name }) {
  const paths = {
    dashboard: <><rect x="3" y="3" width="7" height="7" rx="2"/><rect x="14" y="3" width="7" height="4" rx="2"/><rect x="14" y="11" width="7" height="10" rx="2"/><rect x="3" y="14" width="7" height="7" rx="2"/></>,
    responses: <><path d="M5 6h14M5 12h9M5 18h11"/><circle cx="19" cy="12" r="2"/></>,
    plants: <><path d="M4 21V9l8-5 8 5v12M9 21v-6h6v6M8 10h.01M12 10h.01M16 10h.01"/></>,
    operators: <><circle cx="12" cy="8" r="4"/><path d="M4.5 21a7.5 7.5 0 0 1 15 0"/></>,
    settings: <><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .34 1.88l.06.06-2.83 2.83-.06-.06a1.7 1.7 0 0 0-1.88-.34 1.7 1.7 0 0 0-1.03 1.56V21h-4v-.09A1.7 1.7 0 0 0 8.94 19.4a1.7 1.7 0 0 0-1.88.34l-.06.06-2.83-2.83.06-.06A1.7 1.7 0 0 0 4.6 15 1.7 1.7 0 0 0 3.05 14H3v-4h.09A1.7 1.7 0 0 0 4.6 8.94a1.7 1.7 0 0 0-.34-1.88L4.2 7l2.83-2.83.06.06A1.7 1.7 0 0 0 9 4.6 1.7 1.7 0 0 0 10 3.05V3h4v.09A1.7 1.7 0 0 0 15.06 4.6a1.7 1.7 0 0 0 1.88-.34L17 4.2 19.83 7l-.06.06A1.7 1.7 0 0 0 19.4 9 1.7 1.7 0 0 0 20.95 10H21v4h-.09A1.7 1.7 0 0 0 19.4 15Z"/></>,
  }
  return <svg viewBox="0 0 24 24" aria-hidden="true">{paths[name]}</svg>
}

function SurveyPage({ token }) {
  const [session, setSession] = useState(null)
  const [step, setStep] = useState(0)
  const [ratings, setRatings] = useState({ Sales: {}, Scale: {}, Loading: {} })
  const [state, setState] = useState({ loading: true, error: '', submitting: false, done: null })
  const types = ['Sales', 'Scale', 'Loading']
  const type = types[step]
  useEffect(() => {
    api(`/api/survey/${encodeURIComponent(token)}`).then(data => { setSession(data); setState(s => ({ ...s, loading: false })) })
      .catch(error => setState(s => ({ ...s, loading: false, error: error.message })))
  }, [token])

  const choose = score => setRatings(value => ({ ...value, [type]: { ...value[type], score } }))
  const comment = value => setRatings(current => ({ ...current, [type]: { ...current[type], comment: value } }))
  const advance = async () => {
    if (!ratings[type].score) return setState(s => ({ ...s, error: 'გთხოვთ აირჩიოთ შეფასება.' }))
    setState(s => ({ ...s, error: '' }))
    if (step < 2) return setStep(step + 1)
    setState(s => ({ ...s, submitting: true }))
    try {
      const data = await api(`/api/survey/${encodeURIComponent(token)}`, {
        method: 'POST', body: JSON.stringify({ idempotencyKey: createIdempotencyKey(), ratings: types.map(serviceType => ({ serviceType, score: ratings[serviceType].score, comment: ratings[serviceType].comment || '' })) })
      })
      setState(s => ({ ...s, submitting: false, done: data }))
    } catch (error) { setState(s => ({ ...s, submitting: false, error: error.message })) }
  }

  if (state.loading) return <StatusScreen title="იტვირთება…" detail="ვამოწმებთ QR კოდს" />
  if (state.error && !session) return <StatusScreen title="შეფასება ვერ გაიხსნა" detail={state.error} />
  if (session?.blocked) return <StatusScreen title="შეფასება უკვე დაფიქსირებულია" detail={`შემდეგი შეფასება შესაძლებელი იქნება ${formatDate(session.nextAllowedAt)}.`} />
  if (state.done) return <StatusScreen success title="გმადლობთ შეფასებისთვის!" detail={`თქვენი პასუხი დაცულად შეინახა. შემდეგი შეფასება: ${formatDate(state.done.nextAllowedAt)}.`} />

  return <main className="survey-shell">
    <section className="survey-card">
      <header className="survey-header">
        <span className="survey-brand">KC · KARTULI CEMENT</span>
        <strong>მომსახურების შეფასება</strong>
        <div className="survey-progress" aria-label={`ნაბიჯი ${step + 1} 3-დან`}><i style={{width: `${((step + 1) / 3) * 100}%`}}/></div>
      </header>
      <div className="survey-context"><b>{session.plant}</b><span>{session.operatorName}</span></div>
      <div className="survey-body" key={type}>
        <p className="eyebrow dark">{serviceCopy[type].subtitle}</p>
        <h1>{serviceCopy[type].title}</h1>
        <div className="score-row" role="radiogroup" aria-label="ქულა">
          {[1,2,3,4,5].map(score => <button key={score} className={`score score-${score} ${ratings[type].score === score ? 'selected' : ''}`} onClick={() => choose(score)} aria-label={`${score} — ${scoreLabels[score - 1]}`} aria-pressed={ratings[type].score === score}>{score}</button>)}
        </div>
        <div className="score-legend"><span>ძალიან ცუდი</span><span>← →</span><span>შესანიშნავი</span></div>
        {ratings[type].score && <p className="chosen-label">{scoreLabels[ratings[type].score - 1]}</p>}
        <textarea maxLength="1000" value={ratings[type].comment || ''} onChange={event => comment(event.target.value)} placeholder="კომენტარი (არასავალდებულო)" aria-label="კომენტარი" />
        {state.error && <p className="form-error" role="alert">{state.error}</p>}
        <div className="survey-actions">{step > 0 && <button className="text-button" onClick={() => setStep(step - 1)}>უკან</button>}<button className="primary-button" onClick={advance} disabled={state.submitting}>{state.submitting ? 'იგზავნება…' : step === 2 ? 'შეფასების გაგზავნა' : 'გაგრძელება'}</button></div>
      </div>
      <footer className="survey-footer"><span>ნაბიჯი {step + 1} / 3</span><button className="privacy-link" title={session.privacyNotice}>დაცული კავშირი · კონფიდენციალურობა</button></footer>
    </section>
  </main>
}

function StatusScreen({ title, detail, success }) {
  return <main className="survey-shell"><section className="status-card"><div className={`status-icon ${success ? 'success' : ''}`}>{success ? '✓' : '!'}</div><p className="eyebrow">KARTULI CEMENT</p><h1>{title}</h1><p>{detail}</p></section></main>
}

function AdminApp() {
  const [token, setToken] = useState(adminBypass ? 'test-admin' : localStorage.getItem('factory_service_token'))
  useEffect(() => { const clear = () => setToken(null); window.addEventListener('factory-auth-expired', clear); return () => window.removeEventListener('factory-auth-expired', clear) }, [])
  if (!token) return <Login onAuthenticated={value => { localStorage.setItem('factory_service_token', value); setToken(value) }} />
  return <Portal onLogout={() => {
    localStorage.removeItem('factory_service_token')
    if (adminBypass) window.location.assign('/')
    else setToken(null)
  }} />
}

function Login({ onAuthenticated }) {
  const [code, setCode] = useState('')
  const [challenge, setChallenge] = useState(null)
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const submit = async event => {
    event.preventDefault(); setError(''); setSubmitting(true)
    try {
      if (!challenge) setChallenge(await api('/api/auth/login', { method: 'POST' }))
      else { const result = await api('/api/auth/verify', { method: 'POST', body: JSON.stringify({ challengeId: challenge.challengeId, code }) }); onAuthenticated(result.token) }
    } catch (e) { setError(e.message || 'შესვლა ვერ მოხერხდა') }
    finally { setSubmitting(false) }
  }
  return <main className="login-page"><section className="login-panel"><div className="login-brand"><div className="brand-lock">KC</div><p className="eyebrow">KARTULI CEMENT</p><h1>მომსახურების შეფასება</h1><p>ქარხნების უკუკავშირი ერთ უსაფრთხო სივრცეში.</p></div><form className="login-form" onSubmit={submit}><p className="eyebrow dark">ადმინისტრაციული პორტალი</p><h2>{challenge ? 'SMS კოდის დადასტურება' : 'ადმინისტრატორის შესვლა'}</h2>{challenge ? <><p className="field-help">კოდი გაიგზავნა ნომერზე {challenge.phoneMasked}.</p><label>6-ციფრიანი კოდი<input autoFocus autoComplete="one-time-code" inputMode="numeric" maxLength="6" value={code} onChange={e => setCode(e.target.value.replace(/\D/g, ''))} /></label></> : <p className="field-help login-intro">დააჭირეთ ღილაკს და ავტორიზაციის კოდი ადმინისტრატორის ნომერზე გაიგზავნება.</p>}{error && <p className="form-error" role="alert">{error}</p>}<button className="primary-button" disabled={submitting || (challenge && code.length !== 6)}>{submitting ? 'იგზავნება…' : challenge ? 'დადასტურება' : 'კოდის გაგზავნა'}</button></form></section></main>
}

function Portal({ onLogout }) {
  const [tab, setTab] = useState('dashboard')
  const [user, setUser] = useState(null)
  const tabs = [['dashboard','მიმოხილვა'],['responses','შეფასებები'],['plants','ქარხნები'],['operators','ოპერატორები'], ...(user?.role === 'SystemAdministrator' ? [['settings','პარამეტრები']] : [])]
  useEffect(() => { api('/api/auth/me').then(setUser).catch(() => {}) }, [])
  return <div className="portal"><aside className="sidebar"><div className="sidebar-brand"><Brand/></div><p className="sidebar-label">სამუშაო სივრცე</p><nav>{tabs.map(([key,label]) => <button key={key} className={tab === key ? 'active' : ''} onClick={() => setTab(key)}><i><NavIcon name={key}/></i><span>{label}</span></button>)}</nav><div className="sidebar-foot"><span><i/> სისტემა აქტიურია</span><button className="logout" onClick={onLogout}><NavIcon name="operators"/> გასვლა</button></div></aside><div className="portal-main"><header className="topbar"><div><p className="eyebrow dark">მომსახურების მართვა</p><h1>{tabs.find(x => x[0] === tab)?.[1]}</h1><p className="page-description">{pageCopy[tab]}</p></div><div className="user-chip"><span>{user?.displayName?.[0] || 'A'}</span><div><b>{user?.displayName || 'ადმინისტრატორი'}</b><small>{roleName(user?.role)}</small></div></div></header><main className="content">{tab === 'dashboard' && <Dashboard />}{tab === 'responses' && <Responses canDelete={user?.role === 'SystemAdministrator'} />}{tab === 'plants' && <Directory type="plants" />}{tab === 'operators' && <Directory type="operators" />}{tab === 'settings' && user?.role === 'SystemAdministrator' && <RatingSettings />}</main></div></div>
}

function Dashboard() {
  const [data, setData] = useState(null)
  const [plants, setPlants] = useState([])
  const [operators, setOperators] = useState([])
  const [error, setError] = useState('')
  const [filters, setFilters] = useState({ plantId: '', operatorId: '', from: isoDate(-30), to: isoDate(1) })
  useEffect(() => {
    let active = true
    Promise.all([api('/api/admin/plants'), api('/api/admin/operators')]).then(([p, o]) => { if (active) { setPlants(p); setOperators(o) } }).catch(e => { if (active) setError(e.message) })
    return () => { active = false }
  }, [])
  useEffect(() => {
    let active = true
    setError(''); setData(null)
    api('/api/admin/dashboard?' + queryParams(filters)).then(value => { if (active) setData(value) }).catch(e => { if (active) setError(e.message) })
    return () => { active = false }
  }, [filters])
  return <>
    <section className="filter-bar" aria-label="მიმოხილვის ფილტრები">
      <label>პერიოდი<input type="date" value={filters.from} onChange={e => setFilters({ ...filters, from: e.target.value })}/></label><span>—</span>
      <label><span className="sr-only">დასასრული</span><input type="date" value={filters.to} onChange={e => setFilters({ ...filters, to: e.target.value })}/></label>
      <label>ქარხანა<select value={filters.plantId} onChange={e => setFilters({ ...filters, plantId: e.target.value, operatorId: '' })}><option value="">ყველა</option>{plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
      <label>ოპერატორი<select value={filters.operatorId} onChange={e => setFilters({ ...filters, operatorId: e.target.value })}><option value="">ყველა ოპერატორი</option>{operators.filter(o => !filters.plantId || o.plantId === filters.plantId).map(o => <option key={o.id} value={o.id}>{o.displayName}</option>)}</select></label>
    </section>
    {error && <p className="form-error" role="alert">{error}</p>}
    {!data && !error && <Loading />}
    {data && <><section className="metric-grid"><Metric label="სულ შეფასება" value={formatNumber(data.total)} /><Metric label="გაყიდვები" value={data.sales.average} /><Metric label="სასწორი" value={data.scale.average} /><Metric label="დატვირთვა" value={data.loading.average} /></section><section className="dashboard-grid"><article className="panel trend-panel"><PanelTitle title="ქულის დინამიკა" note="სამივე მომსახურების საშუალო"/><TrendChart data={data.trend}/></article><article className="panel"><PanelTitle title="ქარხნების შედარება" note="საშუალო ქულა"/><div className="plant-bars">{data.plants.map((plant,index) => <div key={plant.id}><span>{index + 1}</span><b>{plant.name}</b><div><i style={{width: (plant.average / 5 * 100) + '%'}}/></div><strong>{plant.average}</strong></div>)}</div></article></section></>}
  </>
}

function RatingSettings() {
  const [days, setDays] = useState('')
  const [state, setState] = useState({ loading: true, saving: false, error: '', saved: false })
  useEffect(() => {
    let active = true
    api('/api/admin/settings').then(data => { if (active) { setDays(data.repeatWindowDays); setState(s => ({ ...s, loading: false })) } })
      .catch(e => { if (active) setState(s => ({ ...s, loading: false, error: e.message })) })
    return () => { active = false }
  }, [])
  const save = async event => {
    event.preventDefault()
    if (!/^(0|[1-9]\d{0,3})$/.test(days) || Number(days) > 3650) {
      setState(s => ({ ...s, error: 'მიუთითეთ დღეების მთელი რაოდენობა 0-დან 3650-მდე.', saved: false })); return
    }
    setState(s => ({ ...s, saving: true, error: '', saved: false }))
    try {
      await api('/api/admin/settings', { method: 'PUT', body: JSON.stringify({ repeatWindowDays: days }) })
      setState(s => ({ ...s, saving: false, saved: true }))
    } catch (e) { setState(s => ({ ...s, saving: false, error: e.message })) }
  }
  return <section className="panel rating-settings">
    <PanelTitle title="შეფასების სიხშირე" note="განმეორებით შეფასებამდე მოცდის პერიოდი" />
    {state.loading ? <Loading /> : <form onSubmit={save}>
      <label htmlFor="repeat-window-days">რამდენი დღის შემდეგ შეიძლება ხელახლა შეფასება?</label>
      <input id="repeat-window-days" type="number" min="0" max="3650" step="1" required value={days} disabled={state.saving} aria-describedby="repeat-window-help" onChange={e => { setDays(e.target.value); setState(s => ({ ...s, saved: false, error: '' })) }} />
      <p className="field-help" id="repeat-window-help">პერიოდი ითვლება ბოლო შეფასებიდან და მოქმედებს ყველა ქარხანასა და ოპერატორზე. 0 დღე გამორთავს მოცდის პერიოდს. ცვლილება მოქმედებს წინა შეფასებებზეც. მომხმარებელი ამოიცნობა იმავე ბრაუზერისა და მოწყობილობის მიხედვით.</p>
      {state.error && <p className="form-error" role="alert">{state.error}</p>}
      {state.saved && <p className="settings-success" role="status">პარამეტრი შენახულია.</p>}
      <button className="primary-button" disabled={state.saving || days === ''}>{state.saving ? 'ინახება…' : 'შენახვა'}</button>
    </form>}
  </section>
}

function Metric({ label, value }) { return <article className="metric"><div className="metric-head"><p>{label}</p><span aria-hidden="true">↗</span></div><div><strong>{value}</strong><small>{label === 'სულ შეფასება' ? 'პასუხი' : 'ქულა / 5'}</small></div></article> }
function PanelTitle({ title, note }) { return <header className="panel-title"><div><h2>{title}</h2><p>{note}</p></div></header> }
function TrendChart({ data }) {
  const chart = { left: 72, right: 816, top: 18, bottom: 250 }
  const x = index => chart.left + index * ((chart.right - chart.left) / Math.max(data.length - 1, 1))
  const y = score => chart.bottom - ((score - 1) / 4) * (chart.bottom - chart.top)
  const points = useMemo(() => data.map((item, index) => `${x(index)},${y(item.average)}`).join(' '), [data])
  const tickStep = Math.max(1, Math.ceil(Math.max(data.length - 1, 1) / 5))
  const dateTicks = data.filter((_, index) => index % tickStep === 0 || index === data.length - 1)

  return <figure className="chart-wrap">
    <svg viewBox="0 0 840 330" role="img" aria-labelledby="trend-chart-title trend-chart-description">
      <title id="trend-chart-title">ქულის დინამიკა თარიღების მიხედვით</title>
      <desc id="trend-chart-description">ჰორიზონტალურ ღერძზე ნაჩვენებია თარიღი, ვერტიკალურზე საშუალო შეფასება ერთიდან ხუთ ქულამდე.</desc>
      <defs><linearGradient id="area" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#00a24a" stopOpacity=".2"/><stop offset="1" stopColor="#00a24a" stopOpacity="0"/></linearGradient></defs>
      <g className="chart-grid">{[1, 2, 3, 4, 5].map(score => <line key={score} x1={chart.left} y1={y(score)} x2={chart.right} y2={y(score)}/>)}</g>
      <g className="chart-y-ticks">{[1, 2, 3, 4, 5].map(score => <text key={score} x="54" y={y(score) + 4}>{score}</text>)}</g>
      <line className="chart-axis" x1={chart.left} y1={chart.top} x2={chart.left} y2={chart.bottom}/>
      <line className="chart-axis" x1={chart.left} y1={chart.bottom} x2={chart.right} y2={chart.bottom}/>
      {data.length > 0 && <polygon className="chart-area" points={`${chart.left},${chart.bottom} ${points} ${chart.right},${chart.bottom}`} fill="url(#area)"/>}
      {data.length > 1 && <polyline className="chart-line" points={points}/>}
      <g className="chart-points">{data.map((item, index) => <circle key={item.date} cx={x(index)} cy={y(item.average)} r="5"><title>{formatAxisDate(item.date)} — საშუალო ქულა: {item.average}</title></circle>)}</g>
      <g className="chart-x-ticks">{dateTicks.map(item => { const index = data.indexOf(item); return <g key={item.date}><line x1={x(index)} y1={chart.bottom} x2={x(index)} y2={chart.bottom + 6}/><text x={x(index)} y="275">{formatAxisDate(item.date)}</text></g> })}</g>
      <text className="chart-axis-title chart-y-title" transform="translate(14 134) rotate(-90)">საშუალო ქულა (1–5)</text>
      <text className="chart-axis-title" x={(chart.left + chart.right) / 2} y="310">თარიღი</text>
    </svg>
    <figcaption><span><i className="chart-legend-line"/>საშუალო შეფასება</span><span><b>X</b> თარიღი</span><span><b>Y</b> ქულა: 1 — ძალიან ცუდი, 5 — შესანიშნავი</span></figcaption>
  </figure>
}

function Responses({ canDelete }) {
  const [data, setData] = useState(null)
  const [plants, setPlants] = useState([])
  const [operators, setOperators] = useState([])
  const [filters, setFilters] = useState(() => responseFilterDefaults())
  const [state, setState] = useState({ loading: true, deleting: '', error: '' })
  const load = values => {
    setState(current => ({ ...current, loading: true, error: '' }))
    return api(`/api/admin/responses?${queryParams({ ...values, pageSize: 200 })}`)
      .then(setData)
      .catch(error => setState(current => ({ ...current, error: error.message })))
      .finally(() => setState(current => ({ ...current, loading: false })))
  }
  useEffect(() => { Promise.all([api('/api/admin/plants'), api('/api/admin/operators')]).then(([plantItems, operatorItems]) => { setPlants(plantItems); setOperators(operatorItems) }).catch(() => {}) }, [])
  useEffect(() => { load(filters) }, [filters])
  const clearFilters = () => setFilters(responseFilterDefaults())
  const remove = async item => {
    if (!confirm(`შეფასება ${formatDateTime(item.submittedAt)}-დან სამუდამოდ წაიშლება. გაგრძელდეს?`)) return
    setState(current => ({ ...current, deleting: item.id, error: '' }))
    try { await api(`/api/admin/responses/${item.id}`, { method: 'DELETE' }); await load(filters) }
    catch (error) { setState(current => ({ ...current, error: error.message })) }
    finally { setState(current => ({ ...current, deleting: '' })) }
  }
  const columns = ['თარიღი','ქარხანა','ოპერატორი','დატვირთვის მომსახურება','გაყიდვების ოპერატორი','სასწორის მომსახურება', ...(canDelete ? [''] : [])]
  const rows = (data?.items || []).map(item => [
    formatDateTime(item.submittedAt), item.plant, item.operator,
    <ScoreValue score={item.loadingScore} comment={item.loadingComment} serviceLabel="დატვირთვის მომსახურება" response={item}/>,
    <ScoreValue score={item.salesScore} comment={item.salesComment} serviceLabel="გაყიდვების ოპერატორი" response={item}/>,
    <ScoreValue score={item.scaleScore} comment={item.scaleComment} serviceLabel="სასწორის მომსახურება" response={item}/>,
    ...(canDelete ? [<button className="row-action delete response-delete" type="button" disabled={state.deleting === item.id} onClick={() => remove(item)}>{state.deleting === item.id ? 'იშლება…' : 'წაშლა'}</button>] : [])
  ])
  return <div className="response-page">
    <section className="filter-bar response-filters" aria-label="შეფასებების ფილტრები">
      <label>თარიღიდან<input type="date" value={filters.from} onChange={event => setFilters({ ...filters, from:event.target.value })}/></label>
      <label>თარიღამდე<input type="date" value={filters.to} onChange={event => setFilters({ ...filters, to:event.target.value })}/></label>
      <label>ქარხანა<select value={filters.plantId} onChange={event => setFilters({ ...filters, plantId:event.target.value, operatorId:'' })}><option value="">ყველა ქარხანა</option>{plants.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
      <label>ოპერატორი<select value={filters.operatorId} onChange={event => setFilters({ ...filters, operatorId:event.target.value })}><option value="">ყველა ოპერატორი</option>{operators.filter(item => !filters.plantId || item.plantId === filters.plantId).map(item => <option key={item.id} value={item.id}>{item.displayName}</option>)}</select></label>
      <label>ქულა<select value={filters.score} onChange={event => setFilters({ ...filters, score:event.target.value })}><option value="">1–5, ყველა</option>{[1,2,3,4,5].map(score => <option key={score} value={score}>{score}</option>)}</select></label>
      <div className="filter-actions"><button className="filter-clear" type="button" onClick={clearFilters}>გასუფთავება</button></div>
    </section>
    {state.error && <p className="directory-error filter-error" role="alert">{state.error}</p>}
    <section className={`panel table-panel response-table ${state.loading ? 'is-loading' : ''}`}><div className="table-toolbar"><div><h2>დეტალური შეფასებები</h2><p>{data?.total || 0} ჩანაწერი</p></div><button className="small-button" type="button" onClick={() => load(filters)} disabled={state.loading}>{state.loading ? 'იტვირთება…' : 'განახლება'}</button></div><DataTable columns={columns} rows={rows}/></section>
  </div>
}

function ScoreValue({ score, comment, serviceLabel, response }) {
  const popoverId = useId()
  return <div className="score-with-comment">
    <span className="score-value" data-score={score}><strong>{score || '—'}</strong>{score ? <small>/5</small> : null}</span>
    {comment && <>
      <button className="comment-trigger" type="button" popoverTarget={popoverId} aria-label={`${serviceLabel}: კომენტარის ნახვა`}>
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5.5 4.75h13a2 2 0 0 1 2 2v7.5a2 2 0 0 1-2 2h-7l-4.25 3v-3H5.5a2 2 0 0 1-2-2v-7.5a2 2 0 0 1 2-2Z"/><path d="M7.5 9h9M7.5 12h6"/></svg>
      </button>
      <aside id={popoverId} className="comment-popover" popover="auto" aria-label={`${serviceLabel} — კომენტარი`}>
        <header><span className="comment-popover-icon" aria-hidden="true">“</span><div><small>შეფასების კომენტარი</small><strong>{response.plant}</strong></div><button type="button" popoverTarget={popoverId} popoverTargetAction="hide" aria-label="კომენტარის დახურვა">×</button></header>
        <dl className="comment-details">
          <div><dt>ქარხანა</dt><dd>{response.plant}</dd></div>
          <div><dt>ოპერატორი</dt><dd>{response.operator}</dd></div>
          <div><dt>თარიღი და დრო</dt><dd>{formatDateTime(response.submittedAt)}</dd></div>
          <div><dt>მომსახურება</dt><dd>{serviceLabel}</dd></div>
          <div><dt>შეფასება</dt><dd><span className="comment-score" data-score={score}>{score}/5</span>{scoreLabels[score - 1] ? ` · ${scoreLabels[score - 1]}` : ''}</dd></div>
        </dl>
        <div className="comment-copy"><small>კომენტარი</small><p>{comment}</p></div>
      </aside>
    </>}
  </div>
}

function Directory({ type }) {
  const [items, setItems] = useState(null)
  const [references, setReferences] = useState({ plants: [] })
  const [editing, setEditing] = useState(null)
  const [error, setError] = useState('')
  const [revision, setRevision] = useState(0)
  const [pending, setPending] = useState(false)
  const [filters, setFilters] = useState({ name:'', address:'', plantId:'' })
  const titles = { plants: 'ქარხნების რეესტრი', operators: 'ოპერატორები' }
  const load = () => setRevision(value => value + 1)
  useEffect(() => {
    let active = true
    setError('')
    Promise.all([api(`/api/admin/${type}`), type === 'operators' ? api('/api/admin/plants') : Promise.resolve([])])
      .then(([records, plants]) => { if (active) { setItems(records); setReferences({ plants }) } })
      .catch(error => { if (active) setError(error.message) })
    return () => { active = false }
  }, [type, revision])
  const filteredItems = useMemo(() => (items || []).filter(item => type === 'plants'
    ? matchesText(item.name, filters.name) && matchesText(item.address, filters.address)
    : (matchesText(item.displayName, filters.name) || matchesText(item.operatorId, filters.name)) && (!filters.plantId || item.plantId === filters.plantId)
  ), [items, filters, type])
  if (!items) return error ? <div className="panel directory-load-error"><p role="alert">{error}</p><button className="small-button" onClick={load}>ხელახლა ცდა</button></div> : <Loading />
  const save = async value => {
    if (pending) return
    setError(''); setPending(true)
    try { await api(`/api/admin/${type}${value.id ? `/${value.id}` : ''}`, { method:value.id ? 'PUT' : 'POST', body:JSON.stringify(value) }); setEditing(null); load() }
    catch(e) { setError(e.message) }
    finally { setPending(false) }
  }
  const remove = async item => {
    if (pending) return
    const message = type === 'operators'
      ? 'ოპერატორი წაიშლება აქტიური სიიდან. არსებული შეფასებები და ანგარიშები შენარჩუნდება. გაგრძელდეს?'
      : 'ქარხანა წაიშლება აქტიური სიიდან. არსებული შეფასებები და ანგარიშები შენარჩუნდება. გაგრძელდეს?'
    if (!confirm(message)) return
    setError(''); setPending(true)
    try { await api(`/api/admin/${type}/${item.id}`, {method:'DELETE'}); load() } catch(e) { setError(e.message) }
    finally { setPending(false) }
  }
  const actions = item => <div className="row-actions"><button className="row-action" disabled={pending} onClick={() => { setError(''); setEditing(item) }}>შეცვლა</button><button className="row-action delete" disabled={pending} onClick={() => remove(item)}>წაშლა</button></div>
  const config = type === 'plants' ? { cols:['დასახელება','მისამართი',''], rows:filteredItems.map(x => [x.name,x.address,actions(x)]) } : { cols:['ოპერატორი','ქარხანა','კლიენტის პროგრამა',''], rows:filteredItems.map(x => [<div className="operator-summary" key={x.id}><strong>{x.displayName}</strong><OperatorId value={x.operatorId}/></div>,x.plantName,<ClientDownload operator={x}/>,actions(x)]) }
  return <div className="directory-page">
    <section className={`filter-bar directory-filters ${type === 'plants' ? 'plant-filters' : ''}`} aria-label={`${titles[type]} — ფილტრები`}>
      <label>
        {type === 'plants' ? 'დასახელება' : 'სახელი ან Operator ID'}
        <input type="search" value={filters.name} onChange={event => setFilters({ ...filters, name:event.target.value })} placeholder={type === 'plants' ? 'ქარხნის დასახელება' : 'ოპერატორის სახელი ან ID'}/>
      </label>
      {type === 'plants' ? <label>
        მისამართი
        <input type="search" value={filters.address} onChange={event => setFilters({ ...filters, address:event.target.value })} placeholder="ქალაქი ან მისამართი"/>
      </label> : <label>
        ქარხანა
        <select value={filters.plantId} onChange={event => setFilters({ ...filters, plantId:event.target.value })}>
          <option value="">ყველა ქარხანა</option>
          {references.plants.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
        </select>
      </label>}
      <button className="filter-clear" type="button" onClick={() => setFilters({ name:'', address:'', plantId:'' })}>გასუფთავება</button>
    </section>
    <section className="panel table-panel">
      <div className="table-toolbar">
        <div><h2>{titles[type]}</h2><p>{filteredItems.length === items.length ? `${items.length} ჩანაწერი` : `${filteredItems.length} / ${items.length} ჩანაწერი`}</p></div>
        <button className="small-button" disabled={pending || (type === 'operators' && !references.plants.length)} onClick={() => { setError(''); setEditing({ timeZone:'Asia/Tbilisi', plantId:references.plants[0]?.id || '' }) }}>+ შექმნა</button>
      </div>
      {type === 'operators' && <div className="client-distribution-note"><span aria-hidden="true">↓</span><div><strong>ერთი პერსონალიზებული EXE თითო ოპერატორზე</strong><p>კლიენტი უკვე შეიცავს საჭირო პარამეტრებს — მომხმარებელს მხოლოდ ინსტალაცია სჭირდება.</p></div></div>}
      {type === 'operators' && !references.plants.length && <p className="directory-help">ოპერატორის დამატებამდე შექმენით ქარხანა ქარხნების რეესტრში.</p>}
      {error && !editing && <p className="directory-error" role="alert">{error} <button className="row-action" onClick={load}>ხელახლა ცდა</button></p>}
      <DataTable columns={config.cols} rows={config.rows}/>
    </section>
    {editing && <DirectoryEditor type={type} value={editing} references={references} error={error} pending={pending} onClose={() => { if (!pending) setEditing(null) }} onSave={save}/>}
  </div>
}

function OperatorId({ value }) {
  const [status, setStatus] = useState('')
  useEffect(() => { setStatus('') }, [value])
  useEffect(() => {
    if (status !== 'copied') return
    const timer = setTimeout(() => setStatus(''), 2500)
    return () => clearTimeout(timer)
  }, [status])
  const copy = async () => {
    try { await navigator.clipboard.writeText(value); setStatus('copied') }
    catch { setStatus('failed') }
  }
  return <div className="operator-id"><div><input aria-label="Operator ID" readOnly value={value} onFocus={event => event.target.select()}/><button type="button" className="row-action" onClick={copy} aria-label="Operator ID-ის კოპირება">{status === 'copied' ? 'დაკოპირებულია' : 'კოპირება'}</button></div><span role="status">{status === 'failed' ? 'კოპირება ვერ მოხერხდა. მონიშნეთ ID და დააკოპირეთ ხელით.' : status === 'copied' ? 'Operator ID დაკოპირებულია.' : ''}</span></div>
}

function ClientDownload({ operator }) {
  const [state, setState] = useState({ busy:false, error:'' })
  const download = async () => {
    setState({ busy:true, error:'' })
    try {
      const response = await fetch('/downloads/FactoryService.Setup.exe', { cache:'no-store' })
      if (!response.ok) throw new Error('Launcher ჯერ არ არის გამოქვეყნებული. გაუშვით client packaging script.')
      const executable = new Uint8Array(await response.arrayBuffer())
      const origin = window.location.origin.replace(/\/$/, '')
      const config = new TextEncoder().encode(JSON.stringify({
        apiBaseUrl: origin,
        payloadUrl: `${origin}/downloads/FactoryServiceClient-win-x64.zip`,
        operatorId: operator.operatorId,
        operatorName: operator.displayName,
        plantName: operator.plantName
      }))
      const marker = new TextEncoder().encode('FSCONFIGv1')
      const length = new Uint8Array(4)
      new DataView(length.buffer).setUint32(0, config.length, true)
      const blob = new Blob([executable, config, marker, length], { type:'application/vnd.microsoft.portable-executable' })
      const href = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = href
      link.download = `FactoryService-${safeFileName(operator.displayName)}-Setup.exe`
      document.body.appendChild(link); link.click(); link.remove()
      setTimeout(() => URL.revokeObjectURL(href), 1000)
      setState({ busy:false, error:'' })
    } catch (error) { setState({ busy:false, error:error.message }) }
  }
  return <div className="client-download"><button type="button" disabled={state.busy} onClick={download}><span aria-hidden="true">↓</span>{state.busy ? 'მზადდება…' : 'EXE-ის გადმოწერა'}</button>{state.error && <small role="alert">{state.error}</small>}</div>
}

function DirectoryEditor({ type, value, references, error, pending, onClose, onSave }) {
  const [form,setForm] = useState(value)
  const set = (key,next) => setForm(current => ({...current,[key]:next}))
  const submit = event => { event.preventDefault(); onSave(form) }
  return <div className="modal-backdrop" role="presentation" onMouseDown={event => event.target === event.currentTarget && onClose()}><form className="editor-modal" onSubmit={submit}><header><div><p className="eyebrow dark">{value.id ? 'ჩანაწერის შეცვლა' : 'ჩანაწერის შექმნა'}</p><h2>{type === 'plants' ? 'ქარხანა' : 'ოპერატორი'}</h2></div><button type="button" onClick={onClose} aria-label="დახურვა">×</button></header>{type === 'plants' && <div className="editor-grid"><label>დასახელება<input required value={form.name || ''} onChange={e=>set('name',e.target.value)}/></label><label className="wide">მისამართი<input value={form.address || ''} onChange={e=>set('address',e.target.value)}/></label><label>დროის სარტყელი<input value={form.timeZone || ''} onChange={e=>set('timeZone',e.target.value)}/></label></div>}{type === 'operators' && <div className="editor-grid">{form.operatorId && <label className="wide">Operator ID<input readOnly value={form.operatorId}/></label>}<label>სახელი და გვარი<input required value={form.displayName || ''} onChange={e=>set('displayName',e.target.value)}/></label><label>ქარხანა<select required value={form.plantId || ''} onChange={e=>set('plantId',e.target.value)}>{references.plants.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label></div>}{error && <p className="form-error">{error}</p>}<footer><span/><button type="button" className="text-button" onClick={onClose}>გაუქმება</button><button className="small-button" disabled={pending}>{pending ? 'ინახება…' : value.id ? 'შეცვლა' : 'შექმნა'}</button></footer></form></div>
}

function DataTable({ columns, rows }) { return <div className="table-scroll"><table><thead><tr>{columns.map(x => <th key={x}>{x}</th>)}</tr></thead><tbody>{rows.length ? rows.map((row,i)=><tr key={i}>{row.map((cell,j)=><td key={j}>{cell}</td>)}</tr>) : <tr><td colSpan={columns.length}>მონაცემები არ მოიძებნა</td></tr>}</tbody></table></div> }
function Loading() { return <div className="loading"><span/><p>მონაცემები იტვირთება…</p></div> }
function Empty({title,detail}) { return <div className="empty"><h2>{title}</h2><p>{detail}</p></div> }

function formatDate(value) { return new Intl.DateTimeFormat('ka-GE',{dateStyle:'long'}).format(new Date(value)) }
function formatAxisDate(value) { return new Intl.DateTimeFormat('ka-GE',{day:'2-digit',month:'2-digit'}).format(new Date(value)) }
function formatDateTime(value) { return new Intl.DateTimeFormat('ka-GE',{dateStyle:'medium',timeStyle:'short'}).format(new Date(value)) }
function formatNumber(value) { return new Intl.NumberFormat('ka-GE').format(value) }
function isoDate(offset) { const date = new Date(); date.setDate(date.getDate()+offset); return date.toISOString().slice(0,10) }
function responseFilterDefaults() { return { from:isoDate(-30), to:isoDate(0), plantId:'', operatorId:'', score:'' } }
function queryParams(values) { return new URLSearchParams(Object.entries(values).filter(([,value]) => value !== '' && value != null)).toString() }
function matchesText(value, search) { return !search.trim() || String(value || '').toLocaleLowerCase('ka-GE').includes(search.trim().toLocaleLowerCase('ka-GE')) }
function roleName(value) { return ({SystemAdministrator:'სისტემის ადმინისტრატორი',PlantAdministrator:'ქარხნის ადმინისტრატორი',ReportViewer:'რეპორტების ნახვა',Auditor:'აუდიტორი'})[value] || value }
function safeFileName(value) { return String(value || 'Operator').normalize('NFKD').replace(/[^\p{L}\p{N}.-]+/gu,'-').replace(/^-+|-+$/g,'').slice(0,60) || 'Operator' }

createRoot(document.getElementById('root')).render(<React.StrictMode><App /></React.StrictMode>)
