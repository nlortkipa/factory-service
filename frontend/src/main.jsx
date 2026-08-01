import React, { useEffect, useMemo, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { api, download } from './api'
import './styles.css'
import './editor.css'

const serviceCopy = {
  Sales: { title: 'გაყიდვების ოპერატორი', subtitle: 'კონკრეტული ოპერატორის შეფასება' },
  Scale: { title: 'სასწორის მომსახურება', subtitle: 'ქარხნის მიხედვით' },
  Loading: { title: 'დატვირთვის მომსახურება', subtitle: 'ქარხნის მიხედვით' },
}
const scoreLabels = ['ძალიან ცუდი', 'ცუდი', 'საშუალო', 'კარგი', 'შესანიშნავი']
const icons = { dashboard: '⌁', responses: '≡', plants: '◇', operators: '◎', devices: '▣', settings: '⚙', audit: '↺' }

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
    <div className="landing-mark">KC</div>
    <p className="eyebrow">KARTULI CEMENT</p>
    <h1>მომსახურების შეფასების სისტემა</h1>
    <p>შეფასების ფორმა იხსნება სამუშაო ეკრანზე ნაჩვენები QR კოდის სკანირებით.</p>
    <a className="primary-button inline" href="/admin">ადმინისტრაციული პორტალი</a>
  </main>
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
        <strong>მომსახურების შეფასება</strong>
        <span>ნაბიჯი {step + 1}/3</span>
      </header>
      <div className="survey-context"><b>{session.plant}</b><span>{session.operatorName} · {session.terminal}</span></div>
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
      <footer className="survey-footer"><span>დაცული კავშირი • HTTPS</span><button className="privacy-link" title={session.privacyNotice}>კონფიდენციალურობა</button></footer>
    </section>
  </main>
}

function StatusScreen({ title, detail, success }) {
  return <main className="survey-shell"><section className="status-card"><div className={`status-icon ${success ? 'success' : ''}`}>{success ? '✓' : '!'}</div><p className="eyebrow">KARTULI CEMENT</p><h1>{title}</h1><p>{detail}</p></section></main>
}

function AdminApp() {
  const [token, setToken] = useState(localStorage.getItem('factory_service_token'))
  useEffect(() => { const clear = () => setToken(null); window.addEventListener('factory-auth-expired', clear); return () => window.removeEventListener('factory-auth-expired', clear) }, [])
  if (!token) return <Login onAuthenticated={value => { localStorage.setItem('factory_service_token', value); setToken(value) }} />
  return <Portal onLogout={() => { localStorage.removeItem('factory_service_token'); setToken(null) }} />
}

function Login({ onAuthenticated }) {
  const [form, setForm] = useState({ username: 'admin', password: 'Admin123!', code: '' })
  const [challenge, setChallenge] = useState(null)
  const [error, setError] = useState('')
  const submit = async event => {
    event.preventDefault(); setError('')
    try {
      if (!challenge) setChallenge(await api('/api/auth/login', { method: 'POST', body: JSON.stringify({ username: form.username, password: form.password }) }))
      else { const result = await api('/api/auth/verify', { method: 'POST', body: JSON.stringify({ challengeId: challenge.challengeId, code: form.code }) }); onAuthenticated(result.token) }
    } catch (e) { setError(e.message || 'შესვლა ვერ მოხერხდა') }
  }
  return <main className="login-page"><section className="login-panel"><div className="login-brand"><div className="brand-lock">KC</div><p className="eyebrow">KARTULI CEMENT</p><h1>მომსახურების შეფასება</h1><p>ქარხნების უკუკავშირი ერთ უსაფრთხო სივრცეში.</p></div><form className="login-form" onSubmit={submit}><p className="eyebrow dark">ადმინისტრაციული პორტალი</p><h2>{challenge ? 'SMS კოდის დადასტურება' : 'სისტემაში შესვლა'}</h2>{challenge ? <><p className="field-help">კოდი გაიგზავნა ნომერზე {challenge.phoneMasked}. განვითარების რეჟიმში კოდი backend-ის ტერმინალში იწერება.</p><label>6-ციფრიანი კოდი<input autoFocus inputMode="numeric" maxLength="6" value={form.code} onChange={e => setForm({ ...form, code: e.target.value.replace(/\D/g, '') })} /></label></> : <><label>მომხმარებელი<input value={form.username} onChange={e => setForm({ ...form, username: e.target.value })} /></label><label>პაროლი<input type="password" value={form.password} onChange={e => setForm({ ...form, password: e.target.value })} /></label></>}{error && <p className="form-error">{error}</p>}<button className="primary-button">{challenge ? 'დადასტურება' : 'კოდის მიღება'}</button><p className="demo-note">დემო: admin / Admin123!</p></form></section></main>
}

function Portal({ onLogout }) {
  const [tab, setTab] = useState('dashboard')
  const [user, setUser] = useState(null)
  const tabs = [['dashboard','მიმოხილვა'],['responses','შეფასებები'],['plants','ქარხნები'],['operators','ოპერატორები'],['devices','მოწყობილობები'],['settings','პარამეტრები'],['audit','აუდიტი']]
  useEffect(() => { api('/api/auth/me').then(setUser).catch(() => {}) }, [])
  return <div className="portal"><aside className="sidebar"><div className="sidebar-brand"><span>KC</span><strong>KARTULI<br/>CEMENT</strong></div><nav>{tabs.map(([key,label]) => <button key={key} className={tab === key ? 'active' : ''} onClick={() => setTab(key)}><i>{icons[key]}</i><span>{label}</span></button>)}</nav><button className="logout" onClick={onLogout}>გასვლა</button></aside><div className="portal-main"><header className="topbar"><div><p className="eyebrow dark">მომსახურების შეფასების სისტემა</p><h1>{tabs.find(x => x[0] === tab)?.[1]}</h1></div><div className="user-chip"><span>{user?.displayName?.[0] || 'A'}</span><div><b>{user?.displayName || 'ადმინისტრატორი'}</b><small>{roleName(user?.role)}</small></div></div></header><main className="content">{tab === 'dashboard' && <Dashboard />}{tab === 'responses' && <Responses />}{tab === 'plants' && <Directory type="plants" />}{tab === 'operators' && <Directory type="operators" />}{tab === 'devices' && <Directory type="workstations" />}{tab === 'settings' && <Settings />}{tab === 'audit' && <Audit />}</main></div></div>
}

function Dashboard() {
  const [data, setData] = useState(null)
  const [plants, setPlants] = useState([])
  const [filters, setFilters] = useState({ plantId: '', from: isoDate(-30), to: isoDate(1) })
  useEffect(() => { api('/api/admin/plants').then(setPlants) }, [])
  useEffect(() => { api(`/api/admin/dashboard?${queryParams(filters)}`).then(setData) }, [filters])
  if (!data) return <Loading />
  return <><section className="filter-bar"><label>პერიოდი<input type="date" value={filters.from} onChange={e => setFilters({ ...filters, from: e.target.value })}/></label><span>—</span><label><span className="sr-only">დასასრული</span><input type="date" value={filters.to} onChange={e => setFilters({ ...filters, to: e.target.value })}/></label><label>ქარხანა<select value={filters.plantId} onChange={e => setFilters({ ...filters, plantId: e.target.value })}><option value="">ყველა</option>{plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label></section><section className="metric-grid"><Metric label="სულ შეფასება" value={formatNumber(data.total)} note={`${data.uniqueDevices} უნიკალური მოწყობილობა`} /><Metric label="გაყიდვები" value={data.sales.average} stars note={`${data.sales.lowPercent}% დაბალი`} /><Metric label="სასწორი" value={data.scale.average} stars note={`${data.scale.lowPercent}% დაბალი`} /><Metric label="დატვირთვა" value={data.loading.average} stars note={`${data.loading.lowPercent}% დაბალი`} /></section><section className="dashboard-grid"><article className="panel trend-panel"><PanelTitle title="ქულის დინამიკა" note="სამივე მომსახურების საშუალო"/><TrendChart data={data.trend}/></article><article className="panel"><PanelTitle title="ქარხნების შედარება" note="საშუალო ქულა"/><div className="plant-bars">{data.plants.map((plant,index) => <div key={plant.id}><span>{index + 1}</span><b>{plant.name}</b><div><i style={{width:`${plant.average / 5 * 100}%`}}/></div><strong>{plant.average}</strong></div>)}</div></article><article className="panel health"><PanelTitle title="მოწყობილობების სტატუსი" note="ბოლო heartbeat"/><div className="health-row">{data.health.map(item => <div key={item.status} className={item.status.toLowerCase()}><span/>{statusName(item.status)}<b>{item.count}</b></div>)}</div><p className="blocked-note">{data.blockedAttempts} დაბლოკილი განმეორებითი მცდელობა პერიოდში</p></article></section></>
}

function Metric({ label, value, stars, note }) { return <article className="metric"><p>{label}</p><div><strong>{value}</strong>{stars && <span className="stars">★★★★★</span>}</div><small>{note}</small></article> }
function PanelTitle({ title, note }) { return <header className="panel-title"><div><h2>{title}</h2><p>{note}</p></div><span>•••</span></header> }
function TrendChart({ data }) {
  const points = useMemo(() => data.map((item,index) => `${40 + index * (760 / Math.max(data.length - 1,1))},${260 - (item.average - 1) / 4 * 210}`).join(' '), [data])
  return <div className="chart-wrap"><svg viewBox="0 0 840 300" role="img" aria-label="ქულის დინამიკის გრაფიკი"><defs><linearGradient id="area" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#00a24a" stopOpacity=".2"/><stop offset="1" stopColor="#00a24a" stopOpacity="0"/></linearGradient></defs><line x1="40" y1="260" x2="800" y2="260"/><polygon points={`40,260 ${points} 800,260`} fill="url(#area)"/><polyline points={points}/>{data.map((item,index) => <circle key={item.date} cx={40 + index * (760 / Math.max(data.length - 1,1))} cy={260 - (item.average - 1) / 4 * 210} r="5"><title>{item.date}: {item.average}</title></circle>)}</svg></div>
}

function Responses() {
  const [data, setData] = useState(null)
  const [filters, setFilters] = useState({ from: isoDate(-30), to: isoDate(1), serviceType: '', score: '', search: '' })
  const load = () => api(`/api/admin/responses?${queryParams(filters)}`).then(setData)
  useEffect(() => { load() }, [])
  const exportFile = format => download(`/api/admin/exports/${format}?${queryParams(filters)}`, `factory-service-report.${format}`)
  return <><section className="filter-bar response-filter"><label>დაწყება<input type="date" value={filters.from} onChange={e => setFilters({...filters,from:e.target.value})}/></label><label>დასასრული<input type="date" value={filters.to} onChange={e => setFilters({...filters,to:e.target.value})}/></label><label>სერვისი<select value={filters.serviceType} onChange={e => setFilters({...filters,serviceType:e.target.value})}><option value="">ყველა</option>{Object.keys(serviceCopy).map(x => <option key={x} value={x}>{serviceCopy[x].title}</option>)}</select></label><label>ქულა<select value={filters.score} onChange={e => setFilters({...filters,score:e.target.value})}><option value="">ყველა</option>{[1,2,3,4,5].map(x => <option key={x}>{x}</option>)}</select></label><label>კომენტარი<input placeholder="ძებნა…" value={filters.search} onChange={e => setFilters({...filters,search:e.target.value})}/></label><button className="small-button" onClick={load}>ფილტრი</button></section><section className="panel table-panel"><div className="table-toolbar"><div><h2>დეტალური შეფასებები</h2><p>{data?.total || 0} ჩანაწერი</p></div><div className="export-buttons">{['csv','xlsx','pdf'].map(x => <button key={x} onClick={() => exportFile(x)}>{x.toUpperCase()}</button>)}</div></div><DataTable columns={['თარიღი','ქარხანა','ოპერატორი','სერვისი','ქულა','კომენტარი']} rows={(data?.items || []).map(x => [formatDateTime(x.submittedAt),x.plant,x.operator,serviceCopy[x.serviceType]?.title,x.score,x.comment || '—'])}/></section></>
}

function Directory({ type }) {
  const [items, setItems] = useState(null)
  const [references, setReferences] = useState({ plants: [], workstations: [] })
  const [editing, setEditing] = useState(null)
  const [error, setError] = useState('')
  const titles = { plants: 'ქარხნების რეესტრი', operators: 'ოპერატორები', workstations: 'სამუშაო ადგილები და ეკრანები' }
  const load = () => api(`/api/admin/${type}`).then(setItems)
  useEffect(() => { load(); if (type !== 'plants') api('/api/admin/plants').then(plants => setReferences(value => ({...value, plants}))); if (type === 'operators') api('/api/admin/workstations').then(workstations => setReferences(value => ({...value, workstations}))) }, [type])
  if (!items) return <Loading />
  const editButton = item => <button className="row-action" onClick={() => setEditing(item)}>რედაქტირება</button>
  const config = type === 'plants' ? { cols:['კოდი','დასახელება','მისამართი','სტატუსი',''], rows:items.map(x => [x.code,x.name,x.address,<Status active={x.isActive}/>,editButton(x)]) } : type === 'operators' ? { cols:['AD მომხმარებელი','სახელი','ქარხანა','სამუშაო ადგილი','სტატუსი',''], rows:items.map(x => [x.adUsername,x.displayName,x.plantName,x.workstation || '—',<Status active={x.isActive}/>,editButton(x)]) } : { cols:['Device ID','კომპიუტერი','ქარხანა','ოპერატორი','ბოლო კავშირი','სტატუსი',''], rows:items.map(x => [x.deviceId,x.computerName,x.plantName,x.activeWindowsUser || '—',x.lastSeen ? formatDateTime(x.lastSeen) : '—',<Status status={x.status}/>,editButton(x)]) }
  const save = async value => { setError(''); try { await api(`/api/admin/${type}${value.id ? `/${value.id}` : ''}`, { method:value.id ? 'PUT' : 'POST', body:JSON.stringify(value) }); setEditing(null); load() } catch(e) { setError(e.message) } }
  const deactivate = async value => { if (!confirm('ჩანაწერი გახდება არააქტიური. გაგრძელდეს?')) return; await api(`/api/admin/${type}/${value.id}`, {method:'DELETE'}); setEditing(null); load() }
  return <><section className="panel table-panel"><div className="table-toolbar"><div><h2>{titles[type]}</h2><p>{items.length} ჩანაწერი</p></div><button className="small-button" onClick={() => setEditing({ isActive:true, timeZone:'Asia/Tbilisi', plantId:references.plants[0]?.id || '', screenModel:'On-screen QR preview' })}>+ დამატება</button></div><DataTable columns={config.cols} rows={config.rows}/></section>{editing && <DirectoryEditor type={type} value={editing} references={references} error={error} onClose={() => setEditing(null)} onSave={save} onDeactivate={deactivate}/>}</>
}

function DirectoryEditor({ type, value, references, error, onClose, onSave, onDeactivate }) {
  const [form,setForm] = useState(value)
  const set = (key,next) => setForm(current => ({...current,[key]:next}))
  const submit = event => { event.preventDefault(); onSave(form) }
  return <div className="modal-backdrop" role="presentation" onMouseDown={event => event.target === event.currentTarget && onClose()}><form className="editor-modal" onSubmit={submit}><header><div><p className="eyebrow dark">{value.id ? 'ჩანაწერის მართვა' : 'ახალი ჩანაწერი'}</p><h2>{type === 'plants' ? 'ქარხანა' : type === 'operators' ? 'ოპერატორი' : 'სამუშაო ადგილი'}</h2></div><button type="button" onClick={onClose} aria-label="დახურვა">×</button></header>{type === 'plants' && <div className="editor-grid"><label>კოდი<input required value={form.code || ''} onChange={e=>set('code',e.target.value)}/></label><label>დასახელება<input required value={form.name || ''} onChange={e=>set('name',e.target.value)}/></label><label className="wide">მისამართი<input value={form.address || ''} onChange={e=>set('address',e.target.value)}/></label><label>დროის სარტყელი<input value={form.timeZone || ''} onChange={e=>set('timeZone',e.target.value)}/></label></div>}{type === 'operators' && <div className="editor-grid"><label className="wide">AD / Windows მომხმარებელი<input required value={form.adUsername || ''} onChange={e=>set('adUsername',e.target.value)}/></label><label>სახელი და გვარი<input required value={form.displayName || ''} onChange={e=>set('displayName',e.target.value)}/></label><label>საჯარო სახელი<input required value={form.publicDisplayName || ''} onChange={e=>set('publicDisplayName',e.target.value)}/></label><label>ქარხანა<select required value={form.plantId || ''} onChange={e=>set('plantId',e.target.value)}>{references.plants.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label>სამუშაო ადგილი<select value={form.workstationId || ''} onChange={e=>set('workstationId',e.target.value || null)}><option value="">არ არის მიბმული</option>{references.workstations.map(x=><option key={x.id} value={x.id}>{x.computerName}</option>)}</select></label></div>}{type === 'workstations' && <div className="editor-grid"><label>Device ID<input required value={form.deviceId || ''} onChange={e=>set('deviceId',e.target.value)}/></label><label>კომპიუტერის სახელი<input required value={form.computerName || ''} onChange={e=>set('computerName',e.target.value)}/></label><label>ქარხანა<select required value={form.plantId || ''} onChange={e=>set('plantId',e.target.value)}>{references.plants.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label>ფანჯარა / ტერმინალი<input value={form.terminalNumber || ''} onChange={e=>set('terminalNumber',e.target.value)}/></label><label>ეკრანის მოდელი<input value={form.screenModel || ''} onChange={e=>set('screenModel',e.target.value)}/></label><label>სერიული ნომერი<input value={form.serial || ''} onChange={e=>set('serial',e.target.value)}/></label><label className="wide">Agent key {form.id && '(ცარიელი — უცვლელი)'}<input required={!form.id} type="password" value={form.agentKey || ''} onChange={e=>set('agentKey',e.target.value)}/></label></div>}<label className="check-label"><input type="checkbox" checked={form.isActive !== false} onChange={e=>set('isActive',e.target.checked)}/> აქტიური ჩანაწერი</label>{error && <p className="form-error">{error}</p>}<footer>{value.id && <button type="button" className="danger-button" onClick={() => onDeactivate(form)}>დეაქტივაცია</button>}<span/><button type="button" className="text-button" onClick={onClose}>გაუქმება</button><button className="small-button">შენახვა</button></footer></form></div>
}

function Settings() {
  const [values, setValues] = useState(null); const [saved, setSaved] = useState(false)
  useEffect(() => { api('/api/admin/settings').then(setValues).catch(e => setValues({ denied: e.message })) }, [])
  if (!values) return <Loading />
  if (values.denied) return <Empty title="წვდომა შეზღუდულია" detail="პარამეტრების შეცვლა მხოლოდ System Administrator-ს შეუძლია."/>
  const fields = [['repeatWindowDays','განმეორების ფანჯარა (დღე)'],['commentMaxLength','კომენტარის მაქს. სიგრძე'],['lowScoreThreshold','დაბალი ქულის ზღვარი'],['privacyNotice','კონფიდენციალურობის ტექსტი']]
  const save = async () => { await api('/api/admin/settings',{method:'PUT',body:JSON.stringify(values)}); setSaved(true); setTimeout(()=>setSaved(false),2500) }
  return <section className="panel settings-panel"><PanelTitle title="სისტემის პარამეტრები" note="ცვლილებები აუდიტის ჟურნალში ინახება"/><div className="settings-grid">{fields.map(([key,label]) => <label key={key}>{label}{key === 'privacyNotice' ? <textarea value={values[key] || ''} onChange={e => setValues({...values,[key]:e.target.value})}/> : <input value={values[key] || ''} onChange={e => setValues({...values,[key]:e.target.value})}/>}</label>)}</div><button className="primary-button inline" onClick={save}>{saved ? 'შენახულია ✓' : 'ცვლილებების შენახვა'}</button></section>
}

function Audit() { const [items,setItems]=useState(null); useEffect(()=>{api('/api/admin/audit').then(setItems)},[]); if(!items)return <Loading/>; return <section className="panel table-panel"><div className="table-toolbar"><div><h2>აუდიტის ჟურნალი</h2><p>უსაფრთხოებისა და ადმინისტრირების მოვლენები</p></div></div><DataTable columns={['დრო','მოქმედება','ობიექტი','Correlation ID']} rows={items.map(x=>[formatDateTime(x.timestamp),x.action,`${x.objectType} · ${x.objectId}`,x.correlationId])}/></section> }
function DataTable({ columns, rows }) { return <div className="table-scroll"><table><thead><tr>{columns.map(x => <th key={x}>{x}</th>)}</tr></thead><tbody>{rows.length ? rows.map((row,i)=><tr key={i}>{row.map((cell,j)=><td key={j}>{cell}</td>)}</tr>) : <tr><td colSpan={columns.length}>მონაცემები არ მოიძებნა</td></tr>}</tbody></table></div> }
function Status({ active, status }) { const value = status || (active ? 'Active' : 'Inactive'); return <span className={`status-badge ${value.toLowerCase()}`}>{statusName(value)}</span> }
function Loading() { return <div className="loading"><span/><p>მონაცემები იტვირთება…</p></div> }
function Empty({title,detail}) { return <div className="empty"><h2>{title}</h2><p>{detail}</p></div> }

function formatDate(value) { return new Intl.DateTimeFormat('ka-GE',{dateStyle:'long'}).format(new Date(value)) }
function formatDateTime(value) { return new Intl.DateTimeFormat('ka-GE',{dateStyle:'medium',timeStyle:'short'}).format(new Date(value)) }
function formatNumber(value) { return new Intl.NumberFormat('ka-GE').format(value) }
function isoDate(offset) { const date = new Date(); date.setDate(date.getDate()+offset); return date.toISOString().slice(0,10) }
function queryParams(values) { return new URLSearchParams(Object.entries(values).filter(([,value]) => value !== '' && value != null)).toString() }
function statusName(value) { return ({Online:'ონლაინ',Warning:'გაფრთხილება',Offline:'ოფლაინ',Active:'აქტიური',Inactive:'არააქტიური'})[value] || value }
function roleName(value) { return ({SystemAdministrator:'სისტემის ადმინისტრატორი',PlantAdministrator:'ქარხნის ადმინისტრატორი',ReportViewer:'რეპორტების ნახვა',Auditor:'აუდიტორი'})[value] || value }

createRoot(document.getElementById('root')).render(<React.StrictMode><App /></React.StrictMode>)
