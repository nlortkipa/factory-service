import React, { createContext, useCallback, useContext, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { createRoot } from 'react-dom/client'
import { api } from './api'
import './styles.css'

const serviceCopy = {
  Sales: { title: 'გაყიდვების ოპერატორი', subtitle: 'კონკრეტული ოპერატორის შეფასება', short: 'გაყიდვები' },
  Scale: { title: 'სასწორის მომსახურება', subtitle: 'ქარხნის მიხედვით', short: 'სასწორი' },
  Loading: { title: 'დატვირთვის მომსახურება', subtitle: 'ქარხნის მიხედვით', short: 'დატვირთვა' },
}
const scoreLabels = ['ძალიან ცუდი', 'ცუდი', 'საშუალო', 'კარგი', 'შესანიშნავი']
const sections = [
  { key: 'dashboard', label: 'მიმოხილვა', description: 'შედეგები და ტენდენციები ერთ ხედში' },
  { key: 'responses', label: 'შეფასებები', description: 'თითოეული შეფასების დეტალური ისტორია' },
  { key: 'plants', label: 'ქარხნები', description: 'ქარხნების მისამართები და მონაცემები' },
  { key: 'operators', label: 'ოპერატორები', description: 'ოპერატორები და პერსონალური კლიენტები' },
  { key: 'settings', label: 'პარამეტრები', description: 'შეფასების სისტემის საერთო წესები', adminOnly: true },
]
const adminBypass = import.meta.env.VITE_ADMIN_BYPASS === 'true'
const isApple = typeof navigator !== 'undefined' && /Mac|iPhone|iPad|iPod/i.test(navigator.platform || navigator.userAgent || '')
const modKey = isApple ? '⌘' : 'Ctrl '
const altKey = isApple ? '⌥' : 'Alt+'

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

/* ───────────────────────── Icons ───────────────────────── */

const iconPaths = {
  dashboard: <><rect x="3.5" y="3.5" width="7" height="7" rx="1.8"/><rect x="13.5" y="3.5" width="7" height="7" rx="1.8"/><rect x="3.5" y="13.5" width="7" height="7" rx="1.8"/><rect x="13.5" y="13.5" width="7" height="7" rx="1.8"/></>,
  responses: <><path d="M5 4.5h14a2 2 0 0 1 2 2V15a2 2 0 0 1-2 2h-7.5L7 20.5V17H5a2 2 0 0 1-2-2V6.5a2 2 0 0 1 2-2Z"/><path d="M7.5 9h9M7.5 12.5h6"/></>,
  plants: <><path d="M3.5 20.5h17M5.5 20.5V9.5l6.5-4.5 6.5 4.5v11"/><path d="M9.75 20.5v-5h4.5v5"/><path d="M9 11.5h.01M12 11.5h.01M15 11.5h.01"/></>,
  operators: <><circle cx="9" cy="8" r="3.5"/><path d="M2.5 20a6.5 6.5 0 0 1 13 0"/><path d="M16 4.8a3.5 3.5 0 0 1 0 6.4M18 14.2a6.5 6.5 0 0 1 3.5 5.8"/></>,
  settings: <><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .34 1.88l.06.06-2.83 2.83-.06-.06a1.7 1.7 0 0 0-1.88-.34 1.7 1.7 0 0 0-1.03 1.56V21h-4v-.09A1.7 1.7 0 0 0 8.94 19.4a1.7 1.7 0 0 0-1.88.34l-.06.06-2.83-2.83.06-.06A1.7 1.7 0 0 0 4.6 15 1.7 1.7 0 0 0 3.05 14H3v-4h.09A1.7 1.7 0 0 0 4.6 8.94a1.7 1.7 0 0 0-.34-1.88L4.2 7l2.83-2.83.06.06A1.7 1.7 0 0 0 9 4.6 1.7 1.7 0 0 0 10 3.05V3h4v.09A1.7 1.7 0 0 0 15.06 4.6a1.7 1.7 0 0 0 1.88-.34L17 4.2 19.83 7l-.06.06A1.7 1.7 0 0 0 19.4 9 1.7 1.7 0 0 0 20.95 10H21v4h-.09A1.7 1.7 0 0 0 19.4 15Z"/></>,
  search: <><circle cx="11" cy="11" r="6.5"/><path d="m20 20-4.4-4.4"/></>,
  plus: <path d="M12 5v14M5 12h14"/>,
  refresh: <><path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3"/><path d="M19.5 4.5v4h-4"/></>,
  trash: <><path d="M4.5 7h15M9.5 7V4.5h5V7M6.5 7l1 13h9l1-13"/><path d="M10 11v5.5M14 11v5.5"/></>,
  edit: <><path d="M4.5 19.5h4l10-10-4-4-10 10v4Z"/><path d="m13 7 4 4"/></>,
  download: <path d="M12 4v11M7.5 10.5 12 15l4.5-4.5M5 19.5h14"/>,
  copy: <><rect x="8.5" y="8.5" width="11" height="11" rx="2"/><path d="M15.5 8.5v-2a2 2 0 0 0-2-2h-7a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h2"/></>,
  logout: <><path d="M14 4.5H6.5a2 2 0 0 0-2 2v11a2 2 0 0 0 2 2H14"/><path d="M10 12h10M16.5 8.5 20 12l-3.5 3.5"/></>,
  sidebar: <><rect x="3.5" y="4.5" width="17" height="15" rx="2.5"/><path d="M9.5 4.5v15M5.8 8h1.6M5.8 10.5h1.6"/></>,
  close: <path d="M6.5 6.5l11 11M17.5 6.5l-11 11"/>,
  car: <><path d="M5 16.5V12l1.8-4.6A2 2 0 0 1 8.66 6h6.68a2 2 0 0 1 1.86 1.4L19 12v4.5"/><path d="M3.5 16.5h17M5 12h14"/><circle cx="8" cy="16.5" r="1.8"/><circle cx="16" cy="16.5" r="1.8"/></>,
  check: <path d="m5 12.5 4.5 4.5L19 7.5"/>,
  filter: <path d="M4 7h16M7 12h10M10 17h4"/>,
  chevron: <path d="m9 6 6 6-6 6"/>,
  chevronDown: <path d="m6 9 6 6 6-6"/>,
  chevronLeft: <path d="m15 6-6 6 6 6"/>,
  chevronUpDown: <path d="m8 9.5 4-4 4 4M8 14.5l4 4 4-4"/>,
  calendar: <><rect x="3.5" y="5" width="17" height="15.5" rx="2.5"/><path d="M3.5 10h17M8 3v4M16 3v4"/></>,
  lock: <><rect x="5" y="10.5" width="14" height="10" rx="2.2"/><path d="M8 10.5V8a4 4 0 0 1 8 0v2.5"/></>,
  qr: <><rect x="4" y="4" width="6" height="6" rx="1.2"/><rect x="14" y="4" width="6" height="6" rx="1.2"/><rect x="4" y="14" width="6" height="6" rx="1.2"/><path d="M14 14h2.5v2.5H14zM17.5 17.5H20V20h-2.5zM14 20h1M20 14v1"/></>,
  message: <><path d="M4 6.5a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H9l-5 4v-14Z"/><path d="M8 9.5h.01M12 9.5h.01M16 9.5h.01"/></>,
  clock: <><circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/></>,
  alert: <><circle cx="12" cy="12" r="8.5"/><path d="M12 8v5M12 16.2h.01"/></>,
  arrowRight: <path d="M5 12h14M13.5 6.5 19 12l-5.5 5.5"/>,
  arrowLeft: <path d="M19 12H5M10.5 6.5 5 12l5.5 5.5"/>,
  shield: <><path d="M12 3.5 19 6v5.5c0 4.3-2.9 7.8-7 9-4.1-1.2-7-4.7-7-9V6l7-2.5Z"/><path d="m9 12 2.2 2.2L15.5 10"/></>,
  star: <path d="m12 4 2.4 5 5.4.6-4 3.7 1.1 5.4L12 16l-4.9 2.7 1.1-5.4-4-3.7 5.4-.6L12 4Z"/>,
  person: <><circle cx="12" cy="8" r="4"/><path d="M4.5 21a7.5 7.5 0 0 1 15 0"/></>,
  ellipsis: <path d="M6 12h.01M12 12h.01M18 12h.01"/>,
}

function Icon({ name, size = 16, className = '', strokeWidth = 1.7 }) {
  return <svg className={`icon ${className}`} width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={strokeWidth} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">{iconPaths[name]}</svg>
}

function Face({ score }) {
  const mouth = {
    1: 'M8 16.6c1.2-1.5 2.5-2.2 4-2.2s2.8.7 4 2.2',
    2: 'M8.6 16c1-.7 2.1-1 3.4-1s2.4.3 3.4 1',
    3: 'M8.6 15.4h6.8',
    4: 'M8.6 14.6c1 .8 2.1 1.2 3.4 1.2s2.4-.4 3.4-1.2',
    5: 'M8 14c1.1 1.8 2.4 2.7 4 2.7s2.9-.9 4-2.7',
  }[score]
  return <svg className="face" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" aria-hidden="true">
    <circle cx="12" cy="12" r="9"/>
    <path d="M9 10.2h.01M15 10.2h.01" strokeWidth="2.6"/>
    <path d={mouth}/>
  </svg>
}

const appName = 'შეფასებების სისტემა'

function AppMark({ size = 'md' }) {
  return <span className={`app-mark ${size}`} aria-hidden="true">
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
      <path d="M5 4.5h14a2 2 0 0 1 2 2V15a2 2 0 0 1-2 2h-7.5L7 20.5V17H5a2 2 0 0 1-2-2V6.5a2 2 0 0 1 2-2Z"/>
      <path d="M12 6.8l.97 2.57 2.74.12-2.14 1.72.72 2.65L12 12.35l-2.29 1.51.72-2.65-2.14-1.72 2.74-.12L12 6.8Z" fill="currentColor" strokeWidth="1"/>
    </svg>
  </span>
}

function Kbd({ children }) { return <kbd className="kbd">{children}</kbd> }

/* ───────────────────────── Shared UI (toasts, alerts) ───────────────────────── */

const UIContext = createContext({ toast: () => {}, confirm: async () => false })
const useUI = () => useContext(UIContext)

function UIProvider({ children }) {
  const [toasts, setToasts] = useState([])
  const [dialog, setDialog] = useState(null)
  const toast = useCallback((message, tone = 'success') => {
    const id = `${Date.now()}-${Math.random().toString(36).slice(2)}`
    setToasts(list => [...list.slice(-2), { id, message, tone }])
    setTimeout(() => setToasts(list => list.map(item => item.id === id ? { ...item, leaving: true } : item)), 2800)
    setTimeout(() => setToasts(list => list.filter(item => item.id !== id)), 3100)
  }, [])
  const confirm = useCallback(options => new Promise(resolve => setDialog({ ...options, resolve })), [])
  const closeDialog = useCallback(value => setDialog(current => { current?.resolve(value); return null }), [])
  const value = useMemo(() => ({ toast, confirm }), [toast, confirm])
  return <UIContext.Provider value={value}>
    {children}
    <div className="toast-region" role="status" aria-live="polite">
      {toasts.map(item => <div key={item.id} className={`toast ${item.tone} ${item.leaving ? 'leaving' : ''}`}>
        <span className="toast-icon"><Icon name={item.tone === 'error' ? 'alert' : 'check'} size={14} strokeWidth={2.2}/></span>{item.message}
      </div>)}
    </div>
    {dialog && <AlertDialog {...dialog} onClose={closeDialog}/>}
  </UIContext.Provider>
}

function useScrollLock() {
  useEffect(() => {
    const body = document.body
    const count = Number(body.dataset.locks || 0) + 1
    body.dataset.locks = count
    body.classList.add('scroll-locked')
    return () => {
      const next = Number(body.dataset.locks || 1) - 1
      body.dataset.locks = next
      if (next <= 0) body.classList.remove('scroll-locked')
    }
  }, [])
}

function useEscape(handler) {
  const ref = useRef(handler)
  ref.current = handler
  useEffect(() => {
    const onKey = event => { if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); ref.current() } }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])
}

function AlertDialog({ title, message, confirmLabel = 'გაგრძელება', cancelLabel = 'გაუქმება', destructive, onClose }) {
  const cancelRef = useRef(null)
  useScrollLock()
  useEscape(() => onClose(false))
  useEffect(() => { cancelRef.current?.focus() }, [])
  const titleId = useId()
  return <div className="overlay alert-overlay" role="presentation" onMouseDown={event => event.target === event.currentTarget && onClose(false)}>
    <div className="alert" role="alertdialog" aria-modal="true" aria-labelledby={titleId}>
      <span className={`alert-icon ${destructive ? 'destructive' : ''}`}><Icon name={destructive ? 'trash' : 'alert'} size={22}/></span>
      <h2 id={titleId}>{title}</h2>
      {message && <p>{message}</p>}
      <div className="alert-actions">
        <button ref={cancelRef} type="button" className="button" onClick={() => onClose(false)}>{cancelLabel}</button>
        <button type="button" className={`button ${destructive ? 'destructive' : 'primary'}`} onClick={() => onClose(true)}>{confirmLabel}</button>
      </div>
    </div>
  </div>
}

function Sheet({ title, onClose, onSubmit, children, footer }) {
  useScrollLock()
  useEscape(onClose)
  const titleId = useId()
  return createPortal(<div className="overlay sheet-overlay" role="presentation" onMouseDown={event => event.target === event.currentTarget && onClose()}>
    <form className="sheet" role="dialog" aria-modal="true" aria-labelledby={titleId} onSubmit={onSubmit}>
      <header className="sheet-header">
        <h2 id={titleId}>{title}</h2>
        <button type="button" className="icon-button" onClick={onClose} aria-label="დახურვა"><Icon name="close"/></button>
      </header>
      <div className="sheet-body">{children}</div>
      <footer className="sheet-footer">{footer}</footer>
    </form>
  </div>, document.body)
}

/* ───────────────────────── App routing ───────────────────────── */

function App() {
  const path = window.location.pathname
  if (path.startsWith('/s/')) return <SurveyPage token={decodeURIComponent(path.slice(3))} />
  if (path.startsWith('/admin')) return <AdminApp />
  return <Landing />
}

function Landing() {
  return <main className="landing">
    <header className="landing-nav">
      <div className="landing-brand"><AppMark/><strong>{appName}</strong></div>
      <a className="button" href="/admin">შესვლა <Icon name="arrowRight" size={14}/></a>
    </header>
    <section className="landing-hero">
      <div className="landing-copy">
        <h1>მომსახურება, რომელსაც <em>უკუკავშირი</em> აუმჯობესებს.</h1>
        <p className="landing-lead">დაასკანერეთ ოპერატორის ეკრანზე ნაჩვენები QR კოდი და შეაფასეთ გამოცდილება რამდენიმე წამში.</p>
        <ol className="landing-steps">
          <li><span><Icon name="qr" size={18}/></span><b>დაასკანერეთ კოდი</b></li>
          <li><span><Icon name="star" size={18}/></span><b>შეაფასეთ მომსახურება</b></li>
        </ol>
      </div>
      <div className="landing-visual" aria-hidden="true">
        <div className="landing-device">
          <div className="landing-device-bar"><i/><i/><i/></div>
          <div className="landing-device-body">
            <div className="landing-qr"><Icon name="qr" size={88} strokeWidth={1.2}/></div>
            <div className="landing-faces">{[1, 2, 3, 4, 5].map(score => <span key={score} data-score={score}><Face score={score}/></span>)}</div>
          </div>
        </div>
      </div>
    </section>
  </main>
}

/* ───────────────────────── Survey (public, mobile-first) ───────────────────────── */

const carPlateSteps = ['Plate', 'Sales', 'Scale', 'Loading']
// Accepts AA123AA or AA-123-AA (any case); always sent as AA123AA.
function normalizeCarPlate(value) { const match = /^([A-Z]{2})-?(\d{3})-?([A-Z]{2})$/.exec(String(value || '').trim().toUpperCase()); return match ? match.slice(1).join('') : null }
function isValidCarPlate(value) { return normalizeCarPlate(value) !== null }
// Live formatting for the plate field: AA-123-AA. Each position only accepts its own
// character type, and the hyphen appears as soon as a group is complete. While deleting,
// a trailing hyphen is only kept if the user left it there, so backspace never gets stuck.
function formatCarPlateInput(value, deleting) {
  const chars = []
  for (const char of String(value || '').toUpperCase()) {
    const position = chars.length
    if (position >= 7) break
    if ((position < 2 || position >= 5 ? /[A-Z]/ : /[0-9]/).test(char)) chars.push(char)
  }
  const keepDash = count => chars.length > count || (chars.length === count && (!deleting || value.endsWith('-')))
  return chars.slice(0, 2).join('') + (keepDash(2) ? '-' : '') + chars.slice(2, 5).join('') + (keepDash(5) ? '-' : '') + chars.slice(5).join('')
}

function GeorgianFlag() {
  const cross = (x, y) => <path d={`M${x - 0.9} ${y - 2.2}h1.8v1.3h1.3v1.8h-1.3v1.3h-1.8v-1.3h-1.3v-1.8h1.3z`}/>
  return <svg className="plate-flag" viewBox="0 0 30 20" aria-hidden="true">
    <rect width="30" height="20" fill="#fff"/>
    <g fill="#e8112d"><rect x="12.5" width="5" height="20"/><rect y="7.5" width="30" height="5"/>{cross(6.25, 3.75)}{cross(23.75, 3.75)}{cross(6.25, 16.25)}{cross(23.75, 16.25)}</g>
    <rect x="0.25" y="0.25" width="29.5" height="19.5" fill="none" stroke="rgba(0,0,0,.25)" strokeWidth="0.5"/>
  </svg>
}

function SurveyPage({ token }) {
  const [session, setSession] = useState(null)
  const [step, setStep] = useState(0)
  const [direction, setDirection] = useState('forward')
  const [carPlate, setCarPlate] = useState('')
  const [ratings, setRatings] = useState({ Sales: {}, Scale: {}, Loading: {} })
  const [state, setState] = useState({ loading: true, error: '', submitting: false, done: null })
  const types = ['Sales', 'Scale', 'Loading']
  const type = carPlateSteps[step]
  const isPlateStep = type === 'Plate'
  const lastStep = carPlateSteps.length - 1
  useEffect(() => {
    api(`/api/survey/${encodeURIComponent(token)}`).then(data => { setSession(data); setState(s => ({ ...s, loading: false })) })
      .catch(error => setState(s => ({ ...s, loading: false, error: error.message })))
  }, [token])

  const choose = score => { setRatings(value => ({ ...value, [type]: { ...value[type], score } })); setState(s => s.error ? { ...s, error: '' } : s) }
  const comment = value => setRatings(current => ({ ...current, [type]: { ...current[type], comment: value } }))
  const back = () => { setDirection('back'); setState(s => ({ ...s, error: '' })); setStep(step - 1) }
  const changePlate = value => { setCarPlate(current => formatCarPlateInput(value, value.length < current.length)); setState(s => s.error ? { ...s, error: '' } : s) }
  const advance = async () => {
    if (isPlateStep) {
      if (!isValidCarPlate(carPlate)) return setState(s => ({ ...s, error: 'ნომერი უნდა იყოს ფორმატით AA123AA ან AA-123-AA.' }))
      setCarPlate(normalizeCarPlate(carPlate))
    } else if (!ratings[type].score) return setState(s => ({ ...s, error: 'გთხოვთ აირჩიოთ შეფასება.' }))
    setState(s => ({ ...s, error: '' }))
    if (step < lastStep) { setDirection('forward'); return setStep(step + 1) }
    setState(s => ({ ...s, submitting: true }))
    try {
      const data = await api(`/api/survey/${encodeURIComponent(token)}`, {
        method: 'POST', body: JSON.stringify({ idempotencyKey: createIdempotencyKey(), carPlate: normalizeCarPlate(carPlate), ratings: types.map(serviceType => ({ serviceType, score: ratings[serviceType].score, comment: ratings[serviceType].comment || '' })) })
      })
      setState(s => ({ ...s, submitting: false, done: data }))
    } catch (error) { setState(s => ({ ...s, submitting: false, error: error.message })) }
  }

  useEffect(() => {
    if (!session || session.blocked || state.done || isPlateStep) return
    const onKey = event => {
      if (isTypingTarget(event.target) || event.metaKey || event.ctrlKey || event.altKey) return
      if (/^[1-5]$/.test(event.key)) { event.preventDefault(); choose(Number(event.key)) }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  if (state.loading) return <StatusScreen tone="loading" title="იტვირთება…" detail="ვამოწმებთ QR კოდს" />
  if (state.error && !session) return <StatusScreen tone="error" title="შეფასება ვერ გაიხსნა" detail={state.error} />
  if (session?.blocked) return <StatusScreen tone="blocked" title="შეფასება უკვე დაფიქსირებულია" detail={`შემდეგი შეფასება შესაძლებელი იქნება ${formatDate(session.nextAllowedAt)}.`} />
  if (state.done) return <StatusScreen tone="success" title="გმადლობთ შეფასებისთვის!" detail={`თქვენი პასუხი დაცულად შეინახა. შემდეგი შეფასება: ${formatDate(state.done.nextAllowedAt)}.`} />

  const selected = ratings[type]?.score
  const commentLength = (ratings[type]?.comment || '').length
  return <main className="survey-shell">
    <header className="survey-top">
      <div className="survey-brand"><AppMark size="sm"/><span>{appName}</span></div>
    </header>
    <section className="survey-card">
      <div className="survey-context">
        <span className="survey-context-icon"><Icon name="plants" size={18}/></span>
        <div><b>{session.plant}</b><span>{session.operatorName}</span></div>
      </div>
      <ol className="survey-steps" aria-label={`ნაბიჯი ${step + 1} ${carPlateSteps.length}-დან`}>
        {carPlateSteps.map((item, index) => <li key={item} className={index < step ? 'done' : index === step ? 'current' : ''} aria-current={index === step ? 'step' : undefined}>
          <i>{index < step ? <Icon name="check" size={11} strokeWidth={2.6}/> : index + 1}</i><span>{item === 'Plate' ? 'ნომერი' : serviceCopy[item].short}</span>
        </li>)}
      </ol>
      <form className={`survey-body ${direction}`} key={type} onSubmit={event => { event.preventDefault(); advance() }} noValidate>
        {isPlateStep ? <>
          <h1>ავტომობილის სახელმწიფო ნომერი</h1>
          <p className="survey-hint">შეფასებამდე მიუთითეთ ავტომობილის ნომერი.</p>
          <label className={`plate-field ${state.error ? 'invalid' : ''}`}>
            <span className="sr-only">სახელმწიფო ნომერი</span>
            <span className="plate-country" aria-hidden="true"><GeorgianFlag/><b>GE</b></span>
            <input type="text" value={carPlate} onChange={event => changePlate(event.target.value)} placeholder="AA-123-AA" maxLength="9"
              autoFocus autoComplete="off" autoCorrect="off" autoCapitalize="characters" spellCheck="false" enterKeyHint="next"
              aria-invalid={Boolean(state.error) || undefined} required />
          </label>
        </> : <>
        <h1>{serviceCopy[type].title}</h1>
        <div className="score-row" role="radiogroup" aria-label="ქულა">
          {[1, 2, 3, 4, 5].map(score => <button type="button" key={score} role="radio" data-score={score} className={`score ${selected === score ? 'selected' : ''}`} onClick={() => choose(score)} aria-label={`${score} — ${scoreLabels[score - 1]}`} aria-checked={selected === score}>
            <Face score={score}/><b>{score}</b>
          </button>)}
        </div>
        <p className={`chosen-label ${selected ? 'visible' : ''}`} data-score={selected || undefined} aria-live="polite">{selected ? scoreLabels[selected - 1] : ''}</p>
        <label className="survey-comment">
          <span className="sr-only">კომენტარი</span>
          <textarea maxLength="1000" rows="3" value={ratings[type].comment || ''} onChange={event => comment(event.target.value)} placeholder="კომენტარი (არასავალდებულო)" />
          {commentLength > 0 && <small>{commentLength} / 1000</small>}
        </label>
        </>}
        {state.error && <p className="form-error" role="alert"><Icon name="alert" size={14}/>{state.error}</p>}
        <div className="survey-actions">
          {step > 0 && <button type="button" className="button large" onClick={back}><Icon name="arrowLeft" size={16}/>უკან</button>}
          <button className="button primary large grow" disabled={state.submitting}>
            {state.submitting ? <><Spinner/>იგზავნება…</> : step === lastStep ? 'შეფასების გაგზავნა' : <>გაგრძელება<Icon name="arrowRight" size={16}/></>}
          </button>
        </div>
      </form>
    </section>
  </main>
}

function StatusScreen({ title, detail, tone }) {
  const icon = { success: 'check', error: 'alert', blocked: 'clock' }[tone]
  return <main className="survey-shell status-shell">
    <section className={`status-card ${tone}`}>
      <div className="status-icon">{tone === 'loading' ? <Spinner large/> : <Icon name={icon} size={30} strokeWidth={tone === 'success' ? 2.4 : 1.9}/>}</div>
      <h1>{title}</h1>
      <p>{detail}</p>
    </section>
  </main>
}

/* ───────────────────────── Admin auth ───────────────────────── */

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
  const { toast } = useUI()
  const [code, setCode] = useState('')
  const [challenge, setChallenge] = useState(null)
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [focused, setFocused] = useState(false)
  const submit = async event => {
    event.preventDefault(); setError(''); setSubmitting(true)
    try {
      if (!challenge) setChallenge(await api('/api/auth/login', { method: 'POST' }))
      else { const result = await api('/api/auth/verify', { method: 'POST', body: JSON.stringify({ challengeId: challenge.challengeId, code }) }); onAuthenticated(result.token) }
    } catch (e) { setError(e.message || 'შესვლა ვერ მოხერხდა') }
    finally { setSubmitting(false) }
  }
  const resend = async () => {
    setError(''); setSubmitting(true)
    try { setChallenge(await api('/api/auth/login', { method: 'POST' })); setCode(''); toast('ახალი კოდი გაიგზავნა') }
    catch (e) { setError(e.message || 'კოდი ვერ გაიგზავნა') }
    finally { setSubmitting(false) }
  }
  return <main className="auth-page">
    <section className="auth-window">
      <div className="auth-head">
        <AppMark size="lg"/>
        <h1>{appName}</h1>
      </div>
      <form className="auth-form" onSubmit={submit}>
        {challenge ? <>
          <p className="auth-hint">კოდი გაიგზავნა: <b>{challenge.phoneMasked}</b></p>
          <label className="otp" data-focused={focused}>
            <span className="sr-only">6-ციფრიანი კოდი</span>
            <input autoFocus autoComplete="one-time-code" inputMode="numeric" pattern="[0-9]*" maxLength="6" value={code}
              onFocus={() => setFocused(true)} onBlur={() => setFocused(false)}
              onChange={e => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} />
            <span className="otp-slots" aria-hidden="true">{Array.from({ length: 6 }, (_, index) => <span key={index} className={`${code[index] ? 'filled' : ''} ${focused && index === Math.min(code.length, 5) ? 'active' : ''}`}>{code[index] || ''}</span>)}</span>
          </label>
        </> : null}
        {error && <p className="form-error" role="alert"><Icon name="alert" size={14}/>{error}</p>}
        <button className="button primary large full" disabled={submitting || (challenge && code.length !== 6)}>
          {submitting ? <><Spinner/>იგზავნება…</> : challenge ? 'დადასტურება' : 'კოდის გაგზავნა'}
        </button>
        {challenge && <div className="auth-links">
          <button type="button" className="link-button" disabled={submitting} onClick={() => { setChallenge(null); setCode(''); setError('') }}>უკან</button>
          <button type="button" className="link-button" disabled={submitting} onClick={resend}>ხელახლა გაგზავნა</button>
        </div>}
      </form>
    </section>
  </main>
}

/* ───────────────────────── Admin portal shell ───────────────────────── */

function readPref(key) { try { return localStorage.getItem(key) } catch { return null } }
function writePref(key, value) { try { localStorage.setItem(key, value) } catch { /* ignore */ } }
function tabFromHash() { const key = window.location.hash.slice(1); return sections.some(section => section.key === key) ? key : 'dashboard' }
function isTypingTarget(target) { return target instanceof HTMLElement && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName)) }

function Portal({ onLogout }) {
  const [user, setUser] = useState(null)
  const [tab, setTab] = useState(tabFromHash)
  const [collapsed, setCollapsed] = useState(() => readPref('fs_sidebar_collapsed') === '1')
  const [paletteOpen, setPaletteOpen] = useState(false)
  const [createRequest, setCreateRequest] = useState(null)
  const isAdmin = user?.role === 'SystemAdministrator'
  const visible = sections.filter(section => !section.adminOnly || isAdmin)
  const activeKey = tab === 'settings' && user && !isAdmin ? 'dashboard' : tab
  const active = sections.find(section => section.key === activeKey)
  const contentRef = useRef(null)

  useEffect(() => { api('/api/auth/me').then(setUser).catch(() => {}) }, [])
  useEffect(() => {
    const onHash = () => setTab(tabFromHash())
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])
  useEffect(() => { document.title = `${active?.label || 'პორტალი'} · ${appName}` }, [active])

  const go = useCallback(key => {
    if (window.location.hash !== `#${key}`) window.location.hash = key
    setTab(key)
    window.scrollTo({ top: 0 })
  }, [])
  const toggleSidebar = () => setCollapsed(value => { writePref('fs_sidebar_collapsed', value ? '0' : '1'); return !value })

  useEffect(() => {
    const onKey = event => {
      const mod = event.metaKey || event.ctrlKey
      const modalOpen = document.querySelector('.overlay')
      if (mod && event.key.toLowerCase() === 'k') { event.preventDefault(); if (!document.querySelector('.alert-overlay, .sheet-overlay')) setPaletteOpen(open => !open); return }
      if (modalOpen || isTypingTarget(event.target)) return
      if (event.key === '/' && !mod) {
        const field = document.querySelector('[data-page-search]')
        if (field) { event.preventDefault(); field.focus() }
        return
      }
      if (event.altKey && !mod && /^Digit[1-5]$/.test(event.code)) {
        const section = visible[Number(event.code.slice(5)) - 1]
        if (section) { event.preventDefault(); go(section.key) }
      }
      if (mod && event.key === '.' ) { event.preventDefault(); toggleSidebar() }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  const commands = [
    ...visible.map((section, index) => ({ id: `go-${section.key}`, group: 'გადასვლა', label: section.label, icon: section.key, hint: `${altKey}${index + 1}`, run: () => go(section.key) })),
    { id: 'new-plant', group: 'მოქმედებები', label: 'ახალი ქარხანა', icon: 'plus', run: () => { go('plants'); setCreateRequest({ type: 'plants', n: Date.now() }) } },
    { id: 'new-operator', group: 'მოქმედებები', label: 'ახალი ოპერატორი', icon: 'plus', run: () => { go('operators'); setCreateRequest({ type: 'operators', n: Date.now() }) } },
    { id: 'search', group: 'მოქმედებები', label: 'ფილტრში ძიება', icon: 'search', hint: '/', run: () => setTimeout(() => document.querySelector('[data-page-search]')?.focus(), 30) },
    { id: 'sidebar', group: 'ხედი', label: collapsed ? 'გვერდითი პანელის ჩვენება' : 'გვერდითი პანელის დამალვა', icon: 'sidebar', hint: `${modKey}.`, run: toggleSidebar },
    { id: 'logout', group: 'ანგარიში', label: 'გასვლა', icon: 'logout', run: onLogout },
  ]

  const displayName = user?.displayName || 'ადმინისტრატორი'
  return <div className={`app ${collapsed ? 'sidebar-collapsed' : ''}`}>
    <aside className="sidebar" aria-label="ნავიგაცია">
      <div className="sidebar-head">
        <AppMark/>
        <strong className="sidebar-title">{appName}</strong>
      </div>
      <button type="button" className="sidebar-search" onClick={() => setPaletteOpen(true)}><Icon name="search" size={14}/><span>ძიება</span><Kbd>{modKey}K</Kbd></button>
      <nav className="sidebar-nav">
        {visible.map((section, index) => <button type="button" key={section.key} className={activeKey === section.key ? 'active' : ''} aria-current={activeKey === section.key ? 'page' : undefined} onClick={() => go(section.key)} title={`${section.label} (${altKey}${index + 1})`}>
          <Icon name={section.key} size={17}/><span>{section.label}</span>
        </button>)}
      </nav>
      <div className="sidebar-foot">
        <div className="user-row">
          <span className="avatar">{displayName[0]}</span>
          <div><b>{displayName}</b><small>{roleName(user?.role) || '—'}</small></div>
          <button type="button" className="icon-button" onClick={onLogout} aria-label="გასვლა" title="გასვლა"><Icon name="logout"/></button>
        </div>
      </div>
    </aside>
    <div className="main">
      <header className="toolbar">
        <button type="button" className="icon-button sidebar-toggle" onClick={toggleSidebar} aria-label={collapsed ? 'გვერდითი პანელის ჩვენება' : 'გვერდითი პანელის დამალვა'} title={`გვერდითი პანელი (${modKey}.)`} aria-pressed={!collapsed}><Icon name="sidebar" size={18}/></button>
        <span className="toolbar-mark"><AppMark size="sm"/></span>
        <div className="toolbar-title"><h1>{active?.label}</h1></div>
        <div className="toolbar-actions">
          <button type="button" className="toolbar-search" onClick={() => setPaletteOpen(true)} aria-label="ძიება და ბრძანებები"><Icon name="search" size={15}/><span>ძიება</span><Kbd>{modKey}K</Kbd></button>
          <UserMenu name={displayName} role={roleName(user?.role)} onLogout={onLogout}/>
        </div>
      </header>
      <main className="content" ref={contentRef} key={activeKey}>
        {activeKey === 'dashboard' && <Dashboard />}
        {activeKey === 'responses' && <Responses canDelete={isAdmin} />}
        {activeKey === 'plants' && <Directory type="plants" createRequest={createRequest} onCreateHandled={() => setCreateRequest(null)} />}
        {activeKey === 'operators' && <Directory type="operators" createRequest={createRequest} onCreateHandled={() => setCreateRequest(null)} />}
        {activeKey === 'settings' && isAdmin && <RatingSettings />}
      </main>
    </div>
    <nav className="tabbar" aria-label="ნავიგაცია">
      {visible.map(section => <button type="button" key={section.key} className={activeKey === section.key ? 'active' : ''} aria-current={activeKey === section.key ? 'page' : undefined} onClick={() => go(section.key)}>
        <Icon name={section.key} size={22} strokeWidth={1.6}/><span>{section.label}</span>
      </button>)}
    </nav>
    {paletteOpen && <CommandPalette commands={commands} onClose={() => setPaletteOpen(false)}/>}
  </div>
}

function UserMenu({ name, role, onLogout }) {
  const id = useId()
  return <div className="user-menu-wrap">
    <button type="button" className="avatar-button" popoverTarget={id} aria-label={`${name} — ანგარიში`}><span className="avatar">{name[0]}</span></button>
    <div id={id} popover="auto" className="menu user-menu">
      <div className="menu-head"><span className="avatar">{name[0]}</span><div><b>{name}</b><small>{role || '—'}</small></div></div>
      <div className="menu-sep"/>
      <button type="button" className="menu-item destructive" onClick={onLogout}><Icon name="logout"/>გასვლა</button>
    </div>
  </div>
}

function CommandPalette({ commands, onClose }) {
  const [query, setQuery] = useState('')
  const [index, setIndex] = useState(0)
  const listRef = useRef(null)
  useScrollLock()
  useEscape(onClose)
  const results = commands.filter(command => matchesText(`${command.label} ${command.detail || ''} ${command.group}`, query))
  useEffect(() => { setIndex(0) }, [query])
  useEffect(() => { listRef.current?.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' }) }, [index])
  const run = command => { onClose(); command.run() }
  const onKeyDown = event => {
    if (event.key === 'ArrowDown') { event.preventDefault(); setIndex(value => results.length ? (value + 1) % results.length : 0) }
    else if (event.key === 'ArrowUp') { event.preventDefault(); setIndex(value => results.length ? (value - 1 + results.length) % results.length : 0) }
    else if (event.key === 'Enter' && results[index]) { event.preventDefault(); run(results[index]) }
  }
  const listId = useId()
  let lastGroup = ''
  return createPortal(<div className="overlay palette-overlay" role="presentation" onMouseDown={event => event.target === event.currentTarget && onClose()}>
    <div className="palette" role="dialog" aria-modal="true" aria-label="ძიება და ბრძანებები">
      <div className="palette-search">
        <Icon name="search" size={18}/>
        <input autoFocus value={query} onChange={event => setQuery(event.target.value)} onKeyDown={onKeyDown} placeholder="ძიება…" role="combobox" aria-expanded="true" aria-controls={listId} aria-activedescendant={results[index] ? `${listId}-${results[index].id}` : undefined}/>
        <Kbd>esc</Kbd>
      </div>
      <div className="palette-list" role="listbox" id={listId} ref={listRef}>
        {results.length === 0 && <p className="palette-empty">შედეგი ვერ მოიძებნა</p>}
        {results.map((command, position) => {
          const header = command.group !== lastGroup ? command.group : null
          lastGroup = command.group
          return <React.Fragment key={command.id}>
            {header && <p className="palette-group">{header}</p>}
            <div id={`${listId}-${command.id}`} role="option" aria-selected={position === index} className="palette-item" onMouseMove={() => setIndex(position)} onClick={() => run(command)}>
              <span className="palette-icon"><Icon name={command.icon} size={16}/></span>
              <span className="palette-text"><b>{command.label}</b>{command.detail && <small>{command.detail}</small>}</span>
              {command.hint && <Kbd>{command.hint}</Kbd>}
            </div>
          </React.Fragment>
        })}
      </div>
    </div>
  </div>, document.body)
}

/* ───────────────────────── Controls ───────────────────────── */

function Spinner({ large }) { return <span className={`spinner ${large ? 'large' : ''}`} aria-hidden="true"/> }

function Field({ label, children, className = '' }) {
  // custom controls (popup menus, date picker, segmented) label themselves; a <label> wrapper would re-trigger them
  const custom = [Select, DatePicker, Segmented].includes(children?.type)
  const Tag = custom ? 'div' : 'label'
  return <Tag className={`field ${className}`}><span className="field-label">{label}</span>{children}</Tag>
}

/* Floating panels (popup menus, calendar) — rendered in <body> so sheets/cards never clip them */
function useFloating(anchorRef, open, { minWidth = 0, height = 300 } = {}) {
  const [pos, setPos] = useState(null)
  useLayoutEffect(() => {
    if (!open) { setPos(null); return }
    const update = () => {
      const rect = anchorRef.current?.getBoundingClientRect()
      if (!rect) return
      const vw = window.innerWidth, vh = window.innerHeight
      const width = Math.min(Math.max(rect.width, minWidth), vw - 16)
      const left = Math.min(Math.max(8, rect.left), vw - width - 8)
      const below = vh - rect.bottom - 12, above = rect.top - 12
      const up = below < Math.min(height, 240) && above > below
      setPos({ left, width, top: up ? undefined : rect.bottom + 5, bottom: up ? vh - rect.top + 5 : undefined, maxHeight: Math.max(140, up ? above : below), up })
    }
    update()
    window.addEventListener('resize', update)
    window.addEventListener('scroll', update, true)
    return () => { window.removeEventListener('resize', update); window.removeEventListener('scroll', update, true) }
  }, [open, anchorRef, minWidth, height])
  return pos
}

function useDismiss(open, refs, onClose) {
  const closeRef = useRef(onClose)
  closeRef.current = onClose
  useEffect(() => {
    if (!open) return
    const onDown = event => { if (refs.every(ref => !ref.current?.contains(event.target))) closeRef.current(false) }
    const onKey = event => { if (event.key === 'Escape') { event.preventDefault(); event.stopImmediatePropagation(); closeRef.current(true) } }
    document.addEventListener('pointerdown', onDown, true)
    window.addEventListener('keydown', onKey, true)
    return () => { document.removeEventListener('pointerdown', onDown, true); window.removeEventListener('keydown', onKey, true) }
  }, [open])
}

function Select({ value, onChange, options, label, placeholder = '—', disabled }) {
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const triggerRef = useRef(null)
  const listRef = useRef(null)
  const listId = useId()
  const selectedIndex = options.findIndex(option => String(option.value) === String(value ?? ''))
  const pos = useFloating(triggerRef, open, { minWidth: 200, height: options.length * 34 + 12 })
  const close = useCallback(refocus => { setOpen(false); if (refocus) triggerRef.current?.focus() }, [])
  useDismiss(open, [triggerRef, listRef], close)
  useEffect(() => { if (open && pos) listRef.current?.focus({ preventScroll: true }) }, [open, pos != null])
  useEffect(() => { if (open) listRef.current?.querySelector('[data-active="true"]')?.scrollIntoView({ block: 'nearest' }) }, [active, open, pos != null])
  const openMenu = () => { if (disabled || !options.length) return; setActive(Math.max(0, selectedIndex)); setOpen(true) }
  const pick = option => { if (option) onChange(option.value); close(true) }
  const onListKey = event => {
    const last = options.length - 1
    if (event.key === 'ArrowDown') { event.preventDefault(); setActive(index => Math.min(last, index + 1)) }
    else if (event.key === 'ArrowUp') { event.preventDefault(); setActive(index => Math.max(0, index - 1)) }
    else if (event.key === 'Home') { event.preventDefault(); setActive(0) }
    else if (event.key === 'End') { event.preventDefault(); setActive(last) }
    else if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); pick(options[active]) }
    else if (event.key === 'Tab') close(false)
    else if (event.key.length === 1) {
      const key = event.key.toLocaleLowerCase('ka-GE')
      const order = [...options.slice(active + 1), ...options.slice(0, active + 1)]
      const match = order.find(option => String(option.label).toLocaleLowerCase('ka-GE').startsWith(key))
      if (match) setActive(options.indexOf(match))
    }
  }
  const current = options[selectedIndex]
  return <>
    <button type="button" ref={triggerRef} className={`popup-button ${open ? 'open' : ''}`} aria-haspopup="listbox" aria-expanded={open} aria-controls={open ? listId : undefined} aria-label={label ? `${label}: ${current?.label ?? placeholder}` : undefined} disabled={disabled}
      onClick={() => open ? close(false) : openMenu()}
      onKeyDown={event => { if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(event.key)) { event.preventDefault(); openMenu() } }}>
      <span className={current ? 'popup-value' : 'popup-value placeholder'}>{current?.label ?? placeholder}</span>
      <Icon name="chevronUpDown" size={13} strokeWidth={2}/>
    </button>
    {open && pos && createPortal(<div ref={listRef} id={listId} className={`floating popup-menu ${pos.up ? 'up' : ''}`} role="listbox" tabIndex={-1} aria-label={label}
      aria-activedescendant={`${listId}-${active}`} onKeyDown={onListKey}
      style={{ left: pos.left, top: pos.top, bottom: pos.bottom, minWidth: pos.width, maxWidth: 'calc(100vw - 16px)', maxHeight: Math.min(pos.maxHeight, 340) }}>
      {options.map((option, index) => <div key={String(option.value)} id={`${listId}-${index}`} role="option" aria-selected={index === selectedIndex} data-active={index === active}
        className="popup-option" onMouseMove={() => setActive(index)} onClick={() => pick(option)}>
        <span className="popup-check">{index === selectedIndex && <Icon name="check" size={13} strokeWidth={2.6}/>}</span>
        <span className="popup-label">{option.label}</span>
      </div>)}
    </div>, document.body)}
  </>
}

const monthNames = ['იანვარი', 'თებერვალი', 'მარტი', 'აპრილი', 'მაისი', 'ივნისი', 'ივლისი', 'აგვისტო', 'სექტემბერი', 'ოქტომბერი', 'ნოემბერი', 'დეკემბერი']
const weekdayNames = ['ორ', 'სა', 'ოთ', 'ხუ', 'პა', 'შა', 'კვ']
function parseISODate(value) { const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value || ''); return match ? new Date(+match[1], +match[2] - 1, +match[3]) : null }
function toISODate(date) { return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}` }
function addDays(date, days) { return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days) }
function sameDay(a, b) { return a && b && a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate() }
function formatPickerDate(value) { const date = parseISODate(value); return date ? `${date.getDate()} ${monthNames[date.getMonth()].slice(0, 3)}. ${date.getFullYear()}` : '' }

function DatePicker({ value, onChange, label }) {
  const [open, setOpen] = useState(false)
  const [focus, setFocus] = useState(() => parseISODate(value) || new Date())
  const triggerRef = useRef(null)
  const panelRef = useRef(null)
  const gridId = useId()
  const selected = parseISODate(value)
  const today = new Date()
  const pos = useFloating(triggerRef, open, { minWidth: 272, height: 330 })
  const close = useCallback(refocus => { setOpen(false); if (refocus) triggerRef.current?.focus() }, [])
  useDismiss(open, [triggerRef, panelRef], close)
  useEffect(() => { if (open && pos) panelRef.current?.querySelector('.calendar-grid')?.focus({ preventScroll: true }) }, [open, pos != null])
  const openPicker = () => { setFocus(parseISODate(value) || new Date()); setOpen(true) }
  const pick = date => { onChange(toISODate(date)); close(true) }
  const shiftMonth = delta => setFocus(date => { const next = new Date(date.getFullYear(), date.getMonth() + delta, 1); const last = new Date(next.getFullYear(), next.getMonth() + 1, 0).getDate(); return new Date(next.getFullYear(), next.getMonth(), Math.min(date.getDate(), last)) })
  const onGridKey = event => {
    const moves = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }
    if (moves[event.key]) { event.preventDefault(); setFocus(date => addDays(date, moves[event.key])) }
    else if (event.key === 'PageUp') { event.preventDefault(); shiftMonth(-1) }
    else if (event.key === 'PageDown') { event.preventDefault(); shiftMonth(1) }
    else if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); pick(focus) }
  }
  const first = new Date(focus.getFullYear(), focus.getMonth(), 1)
  const start = addDays(first, -((first.getDay() + 6) % 7))
  const days = Array.from({ length: 42 }, (_, index) => addDays(start, index))
  return <>
    <button type="button" ref={triggerRef} className={`popup-button date-button ${open ? 'open' : ''}`} aria-haspopup="dialog" aria-expanded={open} aria-label={label ? `${label}: ${formatPickerDate(value)}` : undefined}
      onClick={() => open ? close(false) : openPicker()}
      onKeyDown={event => { if (event.key === 'ArrowDown') { event.preventDefault(); openPicker() } }}>
      <Icon name="calendar" size={14}/>
      <span className="popup-value">{formatPickerDate(value) || '—'}</span>
    </button>
    {open && pos && createPortal(<div ref={panelRef} className={`floating calendar ${pos.up ? 'up' : ''}`} role="dialog" aria-label={label} style={{ left: pos.left, top: pos.top, bottom: pos.bottom }}>
      <header className="calendar-head">
        <b>{monthNames[focus.getMonth()]} {focus.getFullYear()}</b>
        <div>
          <button type="button" className="icon-button" onClick={() => shiftMonth(-1)} aria-label="წინა თვე"><Icon name="chevronLeft" size={15}/></button>
          <button type="button" className="icon-button" onClick={() => shiftMonth(1)} aria-label="შემდეგი თვე"><Icon name="chevron" size={15}/></button>
        </div>
      </header>
      <div className="calendar-weekdays" aria-hidden="true">{weekdayNames.map(name => <span key={name}>{name}</span>)}</div>
      <div className="calendar-grid" role="grid" tabIndex={0} aria-activedescendant={`${gridId}-${toISODate(focus)}`} onKeyDown={onGridKey}>
        {days.map(date => <button type="button" tabIndex={-1} key={toISODate(date)} id={`${gridId}-${toISODate(date)}`} role="gridcell" aria-selected={sameDay(date, selected)}
          className={['day', date.getMonth() !== focus.getMonth() && 'outside', sameDay(date, today) && 'today', sameDay(date, selected) && 'selected', sameDay(date, focus) && 'focused'].filter(Boolean).join(' ')}
          onClick={() => pick(date)}>{date.getDate()}</button>)}
      </div>
    </div>, document.body)}
  </>
}

function SearchInput(props) {
  return <span className="search-input"><Icon name="search" size={14}/><input type="search" {...props}/></span>
}

function Segmented({ options, value, onChange, label, className = '' }) {
  return <div className={`segmented ${className}`} role="radiogroup" aria-label={label}>
    {options.map(option => <button type="button" key={option.value} role="radio" aria-checked={value === option.value} className={value === option.value ? 'active' : ''} onClick={() => onChange(option.value)}>{option.label}</button>)}
  </div>
}

function FilterBar({ label, activeCount = 0, lead, children, actions }) {
  const [open, setOpen] = useState(false)
  return <section className={`filter-bar ${open ? 'open' : ''}`} aria-label={label}>
    {lead}
    <button type="button" className="button filter-toggle" aria-expanded={open} onClick={() => setOpen(value => !value)}>
      <Icon name="filter" size={15}/>ფილტრები{activeCount > 0 && <span className="badge">{activeCount}</span>}<Icon name="chevronDown" size={13} className="chev"/>
    </button>
    <div className="filter-fields">{children}{actions && <div className="filter-actions">{actions}</div>}</div>
  </section>
}

function RangePicker({ filters, setFilters, toOffset }) {
  const presets = [7, 30, 90]
  const current = presets.find(days => filters.from === isoDate(-days) && filters.to === isoDate(toOffset))
  return <Segmented label="პერიოდი" className="range-picker" value={current || 'custom'} onChange={days => days !== 'custom' && setFilters({ ...filters, from: isoDate(-days), to: isoDate(toOffset) })}
    options={[...presets.map(days => ({ value: days, label: `${days} დღე` })), ...(current ? [] : [{ value: 'custom', label: 'სხვა' }])]}/>
}

/* ───────────────────────── Dashboard ───────────────────────── */

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
  const activeCount = (filters.plantId ? 1 : 0) + (filters.operatorId ? 1 : 0)
  return <div className="page">
    <FilterBar label="მიმოხილვის ფილტრები" activeCount={activeCount} lead={<RangePicker filters={filters} setFilters={setFilters} toOffset={1}/>}>
      <Field label="დან"><DatePicker label="დან" value={filters.from} onChange={from => setFilters({ ...filters, from })}/></Field>
      <Field label="მდე"><DatePicker label="მდე" value={filters.to} onChange={to => setFilters({ ...filters, to })}/></Field>
      <Field label="ქარხანა"><Select label="ქარხანა" value={filters.plantId} onChange={plantId => setFilters({ ...filters, plantId, operatorId: '' })} options={[{ value: '', label: 'ყველა' }, ...plants.map(p => ({ value: p.id, label: p.name }))]}/></Field>
      <Field label="ოპერატორი"><Select label="ოპერატორი" value={filters.operatorId} onChange={operatorId => setFilters({ ...filters, operatorId })} options={[{ value: '', label: 'ყველა' }, ...operators.filter(o => !filters.plantId || o.plantId === filters.plantId).map(o => ({ value: o.id, label: o.displayName }))]}/></Field>
    </FilterBar>
    {error && <p className="banner error" role="alert"><Icon name="alert"/>{error}</p>}
    {!data && !error && <Loading />}
    {data && <>
      <section className="metric-grid">
        <Metric icon="responses" label="სულ შეფასება" value={formatNumber(data.total)} unit="პასუხი" />
        <Metric icon="operators" label="გაყიდვები" value={data.sales.average} unit="/ 5" score />
        <Metric icon="dashboard" label="სასწორი" value={data.scale.average} unit="/ 5" score />
        <Metric icon="plants" label="დატვირთვა" value={data.loading.average} unit="/ 5" score />
      </section>
      <section className="dashboard-grid">
        <article className="card trend-card">
          <CardHeader title="ქულის დინამიკა"/>
          {data.trend.length ? <TrendChart data={data.trend}/> : <Empty icon="dashboard" title="მონაცემები არ არის" />}
        </article>
        <article className="card">
          <CardHeader title="ქარხნების შედარება"/>
          {data.plants.length ? <ol className="plant-bars">{data.plants.map((plant, index) => <li key={plant.id}>
            <span className="rank">{index + 1}</span>
            <div className="plant-bar-body">
              <div className="plant-bar-head"><b>{plant.name}</b><strong data-score={scoreBucket(plant.average)}>{plant.average}</strong></div>
              <div className="bar-track"><i data-score={scoreBucket(plant.average)} style={{ width: `${Math.max(0, Math.min(100, (Number(plant.average) || 0) / 5 * 100))}%` }}/></div>
            </div>
          </li>)}</ol> : <Empty icon="plants" title="მონაცემები არ არის" />}
        </article>
      </section>
    </>}
  </div>
}

function Metric({ icon, label, value, unit, score }) {
  const numeric = Number(value)
  const bucket = score && Number.isFinite(numeric) && numeric > 0 ? scoreBucket(numeric) : undefined
  return <article className="card metric" data-score={bucket}>
    <div className="metric-head"><span className="metric-icon"><Icon name={icon} size={15}/></span><p>{label}</p>{bucket && <span className="metric-caption">{scoreLabels[bucket - 1]}</span>}</div>
    <div className="metric-value"><strong>{value ?? '—'}</strong><small>{unit}</small></div>
    {score && <div className="metric-meter" aria-hidden="true"><i style={{ width: `${Number.isFinite(numeric) ? Math.max(0, Math.min(100, numeric / 5 * 100)) : 0}%` }}/></div>}
  </article>
}

function CardHeader({ title, note, children }) {
  return <header className="card-header"><div><h2>{title}</h2>{note && <p>{note}</p>}</div>{children && <div className="card-actions">{children}</div>}</header>
}

function useElementWidth() {
  const ref = useRef(null)
  const [width, setWidth] = useState(0)
  useEffect(() => {
    const element = ref.current
    if (!element) return
    setWidth(element.clientWidth)
    const observer = new ResizeObserver(([entry]) => setWidth(Math.round(entry.contentRect.width)))
    observer.observe(element)
    return () => observer.disconnect()
  }, [])
  return [ref, width]
}

function TrendChart({ data }) {
  const [ref, measured] = useElementWidth()
  const [hover, setHover] = useState(null)
  const gradientId = `trend-${useId().replace(/[^a-zA-Z0-9]/g, '')}`
  const width = Math.max(measured || 640, 260)
  const height = width < 520 ? 220 : 280
  const pad = { left: 34, right: 14, top: 14, bottom: 30 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom
  const x = index => pad.left + (data.length > 1 ? index * innerW / (data.length - 1) : innerW / 2)
  const y = score => pad.top + (1 - (Math.max(1, Math.min(5, Number(score) || 1)) - 1) / 4) * innerH
  const points = data.map((item, index) => [x(index), y(item.average)])
  const line = points.map(([px, py], index) => `${index ? 'L' : 'M'}${px.toFixed(1)},${py.toFixed(1)}`).join(' ')
  const area = points.length ? `${line} L${points.at(-1)[0].toFixed(1)},${pad.top + innerH} L${points[0][0].toFixed(1)},${pad.top + innerH} Z` : ''
  const maxTicks = Math.max(2, Math.floor(innerW / 70))
  const tickStep = Math.max(1, Math.ceil(data.length / maxTicks))
  const ticks = data.map((item, index) => ({ item, index })).filter(({ index }) => index % tickStep === 0)
  const onMove = event => {
    const rect = event.currentTarget.getBoundingClientRect()
    const relative = (event.clientX - rect.left) / rect.width * width
    const index = data.length > 1 ? Math.round((relative - pad.left) / (innerW / (data.length - 1))) : 0
    setHover(Math.max(0, Math.min(data.length - 1, index)))
  }
  const hovered = hover != null ? data[hover] : null
  return <figure className="chart" ref={ref}>
    <svg width="100%" height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label="ქულის დინამიკა თარიღების მიხედვით, საშუალო შეფასება ერთიდან ხუთ ქულამდე" onPointerMove={onMove} onPointerLeave={() => setHover(null)}>
      <defs><linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="var(--accent)" stopOpacity=".22"/><stop offset="1" stopColor="var(--accent)" stopOpacity="0"/></linearGradient></defs>
      <g className="chart-grid">{[1, 2, 3, 4, 5].map(score => <g key={score}><line x1={pad.left} x2={width - pad.right} y1={y(score)} y2={y(score)}/><text x={pad.left - 12} y={y(score) + 4}>{score}</text></g>)}</g>
      <g className="chart-x">{ticks.map(({ item, index }) => <text key={item.date} x={x(index)} y={height - 8}>{formatAxisDate(item.date)}</text>)}</g>
      {area && <path d={area} fill={`url(#${gradientId})`}/>}
      {points.length > 1 && <path className="chart-line" d={line}/>}
      {hovered && <line className="chart-rule" x1={x(hover)} x2={x(hover)} y1={pad.top} y2={pad.top + innerH}/>}
      <g className="chart-points">{points.map(([px, py], index) => <circle key={data[index].date} cx={px} cy={py} r={hover === index ? 5 : data.length > 40 ? 0 : 3}/>)}</g>
    </svg>
    {hovered && <div className="chart-tooltip" style={{ left: `${x(hover) / width * 100}%`, top: y(hovered.average) }}>
      <small>{formatDate(hovered.date)}</small><b data-score={scoreBucket(hovered.average)}>{hovered.average}</b><span>საშუალო ქულა</span>
    </div>}
  </figure>
}

/* ───────────────────────── Settings ───────────────────────── */

function RatingSettings() {
  const { toast } = useUI()
  const [days, setDays] = useState('')
  const [state, setState] = useState({ loading: true, saving: false, error: '', saved: false })
  useEffect(() => {
    let active = true
    api('/api/admin/settings').then(data => { if (active) { setDays(String(data.repeatWindowDays)); setState(s => ({ ...s, loading: false })) } })
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
      toast('პარამეტრი შენახულია')
    } catch (e) { setState(s => ({ ...s, saving: false, error: e.message })) }
  }
  const update = value => { setDays(value); setState(s => ({ ...s, saved: false, error: '' })) }
  return <div className="page settings-page">
    <section className="card settings-card">
      {state.loading ? <Loading /> : <form onSubmit={save}>
        <div className="settings-row">
          <div className="settings-row-text">
            <label htmlFor="repeat-window-days">ხელახლა შეფასება</label>
            <p id="repeat-window-help">დღეების რაოდენობა · 0 — შეზღუდვის გარეშე</p>
          </div>
          <div className="stepper">
            <button type="button" className="stepper-button" aria-label="შემცირება" disabled={state.saving || !(Number(days) > 0)} onClick={() => update(String(Math.max(0, (Number(days) || 0) - 1)))}>−</button>
            <input id="repeat-window-days" type="number" inputMode="numeric" min="0" max="3650" step="1" required value={days} disabled={state.saving} aria-describedby="repeat-window-help" onChange={e => update(e.target.value)} />
            <button type="button" className="stepper-button" aria-label="გაზრდა" disabled={state.saving || Number(days) >= 3650} onClick={() => update(String(Math.min(3650, (Number(days) || 0) + 1)))}>+</button>
            <span className="stepper-unit">დღე</span>
          </div>
        </div>
        <div className="settings-row presets">
          <Segmented label="სწრაფი არჩევა" value={days} onChange={update} options={['0', '1', '7', '14', '30'].map(value => ({ value, label: value === '0' ? '0' : `${value} დღე` }))}/>
        </div>
        {state.error && <p className="form-error" role="alert"><Icon name="alert" size={14}/>{state.error}</p>}
        <div className="settings-actions">
          <button className="button primary" disabled={state.saving || days === ''}>{state.saving ? <><Spinner/>ინახება…</> : 'შენახვა'}</button>
        </div>
      </form>}
    </section>
  </div>
}

/* ───────────────────────── Responses ───────────────────────── */

function Responses({ canDelete }) {
  const { toast, confirm } = useUI()
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
    const ok = await confirm({ title: 'წაიშალოს შეფასება?', message: `შეფასება ${formatDateTime(item.submittedAt)}-დან სამუდამოდ წაიშლება. ამ მოქმედების გაუქმება შეუძლებელია.`, confirmLabel: 'წაშლა', destructive: true })
    if (!ok) return
    setState(current => ({ ...current, deleting: item.id, error: '' }))
    try { await api(`/api/admin/responses/${item.id}`, { method: 'DELETE' }); toast('შეფასება წაიშალა'); await load(filters) }
    catch (error) { setState(current => ({ ...current, error: error.message })) }
    finally { setState(current => ({ ...current, deleting: '' })) }
  }
  const activeCount = ['plantId', 'operatorId', 'score'].filter(key => filters[key]).length
  const columns = ['თარიღი', 'ავტ. ნომერი', 'ქარხანა', 'ოპერატორი', serviceCopy.Loading.short, serviceCopy.Sales.short, serviceCopy.Scale.short, ...(canDelete ? [''] : [])]
  const rows = (data?.items || []).map(item => ({
    key: item.id,
    cells: [
      <span className="cell-date">{formatDateTime(item.submittedAt)}</span>, <PlateValue value={item.carPlate}/>, item.plant, item.operator,
      <ScoreValue score={item.loadingScore} comment={item.loadingComment} serviceLabel={serviceCopy.Loading.title} response={item}/>,
      <ScoreValue score={item.salesScore} comment={item.salesComment} serviceLabel={serviceCopy.Sales.title} response={item}/>,
      <ScoreValue score={item.scaleScore} comment={item.scaleComment} serviceLabel={serviceCopy.Scale.title} response={item}/>,
      ...(canDelete ? [<div className="row-actions"><button className="icon-button danger" type="button" disabled={state.deleting === item.id} onClick={() => remove(item)} aria-label="შეფასების წაშლა" title="წაშლა">{state.deleting === item.id ? <Spinner/> : <Icon name="trash"/>}</button></div>] : [])
    ]
  }))
  return <div className="page">
    <FilterBar label="შეფასებების ფილტრები" activeCount={activeCount} lead={<RangePicker filters={filters} setFilters={setFilters} toOffset={0}/>}
      actions={<button className="button" type="button" onClick={clearFilters}>გასუფთავება</button>}>
      <Field label="დან"><DatePicker label="დან" value={filters.from} onChange={from => setFilters({ ...filters, from })}/></Field>
      <Field label="მდე"><DatePicker label="მდე" value={filters.to} onChange={to => setFilters({ ...filters, to })}/></Field>
      <Field label="ქარხანა"><Select label="ქარხანა" value={filters.plantId} onChange={plantId => setFilters({ ...filters, plantId, operatorId: '' })} options={[{ value: '', label: 'ყველა' }, ...plants.map(item => ({ value: item.id, label: item.name }))]}/></Field>
      <Field label="ოპერატორი"><Select label="ოპერატორი" value={filters.operatorId} onChange={operatorId => setFilters({ ...filters, operatorId })} options={[{ value: '', label: 'ყველა' }, ...operators.filter(item => !filters.plantId || item.plantId === filters.plantId).map(item => ({ value: item.id, label: item.displayName }))]}/></Field>
      <Field label="ქულა" className="score-field"><Segmented label="ქულა" value={String(filters.score)} onChange={score => setFilters({ ...filters, score })} options={[{ value: '', label: 'ყველა' }, ...[1, 2, 3, 4, 5].map(score => ({ value: String(score), label: String(score) }))]}/></Field>
    </FilterBar>
    {state.error && <p className="banner error" role="alert"><Icon name="alert"/>{state.error}</p>}
    <section className={`card table-card ${state.loading ? 'is-loading' : ''}`}>
      <CardHeader title="დეტალური შეფასებები" note={`${formatNumber(data?.total || 0)} ჩანაწერი`}>
        <button className="button" type="button" onClick={() => load(filters)} disabled={state.loading}>{state.loading ? <Spinner/> : <Icon name="refresh" size={14}/>}<span className="hide-sm">განახლება</span></button>
      </CardHeader>
      {!data && state.loading ? <Loading/> : <DataTable className="responses-table" columns={columns} rows={rows} empty={<Empty icon="responses" title="შეფასებები ვერ მოიძებნა" />}/>}
    </section>
  </div>
}

function PlateValue({ value }) {
  return value ? <span className="plate-chip">{value}</span> : <span className="muted">—</span>
}

function ScoreValue({ score, comment, serviceLabel, response }) {
  const popoverId = useId()
  return <div className="score-with-comment">
    <span className="score-chip" data-score={score || undefined}><strong>{score || '—'}</strong>{score ? <small>/5</small> : null}</span>
    {comment && <>
      <button className="comment-trigger" type="button" popoverTarget={popoverId} aria-label={`${serviceLabel}: კომენტარის ნახვა`} title="კომენტარის ნახვა">
        <Icon name="responses" size={14}/>
      </button>
      <aside id={popoverId} className="comment-popover" popover="auto" aria-label={`${serviceLabel} — კომენტარი`}>
        <header>
          <span className="comment-score" data-score={score}>{score}</span>
          <div><small>{serviceLabel}</small><strong>{scoreLabels[score - 1] || 'კომენტარი'}</strong></div>
          <button type="button" className="icon-button" popoverTarget={popoverId} popoverTargetAction="hide" aria-label="კომენტარის დახურვა"><Icon name="close"/></button>
        </header>
        <blockquote>{comment}</blockquote>
        <dl className="comment-details">
          <div><dt>ავტ. ნომერი</dt><dd>{response.carPlate || '—'}</dd></div>
          <div><dt>ქარხანა</dt><dd>{response.plant}</dd></div>
          <div><dt>ოპერატორი</dt><dd>{response.operator}</dd></div>
          <div><dt>თარიღი და დრო</dt><dd>{formatDateTime(response.submittedAt)}</dd></div>
        </dl>
      </aside>
    </>}
  </div>
}

/* ───────────────────────── Plants & operators ───────────────────────── */

function Directory({ type, createRequest, onCreateHandled }) {
  const { toast, confirm } = useUI()
  const [items, setItems] = useState(null)
  const [references, setReferences] = useState({ plants: [] })
  const [editing, setEditing] = useState(null)
  const [error, setError] = useState('')
  const [revision, setRevision] = useState(0)
  const [pending, setPending] = useState(false)
  const [filters, setFilters] = useState({ name: '', address: '', plantId: '' })
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
    : matchesText(item.displayName, filters.name) && (!filters.plantId || item.plantId === filters.plantId)
  ), [items, filters, type])
  const canCreate = type === 'plants' || references.plants.length > 0
  const openCreate = useCallback(() => {
    if (!canCreate) { toast('ოპერატორის დამატებამდე შექმენით ქარხანა', 'error'); return }
    setError(''); setEditing({ timeZone: 'Asia/Tbilisi', plantId: references.plants[0]?.id || '' })
  }, [canCreate, references, toast])
  useEffect(() => {
    if (!createRequest || createRequest.type !== type || !items) return
    onCreateHandled?.()
    openCreate()
  }, [createRequest, items, type, openCreate, onCreateHandled])

  if (!items) return error
    ? <div className="page"><div className="card load-error"><Empty icon="alert" title="მონაცემები ვერ ჩაიტვირთა" detail={error}/><button className="button" onClick={load}><Icon name="refresh" size={14}/>ხელახლა ცდა</button></div></div>
    : <Loading />
  const save = async value => {
    if (pending) return
    setError(''); setPending(true)
    try {
      await api(`/api/admin/${type}${value.id ? `/${value.id}` : ''}`, { method: value.id ? 'PUT' : 'POST', body: JSON.stringify(value) })
      setEditing(null); load()
      toast(value.id ? 'ცვლილებები შენახულია' : type === 'plants' ? 'ქარხანა დაემატა' : 'ოპერატორი დაემატა')
    }
    catch (e) { setError(e.message) }
    finally { setPending(false) }
  }
  const remove = async item => {
    if (pending) return
    const ok = await confirm({
      title: type === 'operators' ? `წაიშალოს „${item.displayName}“?` : `წაიშალოს „${item.name}“?`,
      message: type === 'operators'
        ? 'ოპერატორი წაიშლება აქტიური სიიდან. არსებული შეფასებები და ანგარიშები შენარჩუნდება.'
        : 'ქარხანა წაიშლება აქტიური სიიდან. არსებული შეფასებები და ანგარიშები შენარჩუნდება.',
      confirmLabel: 'წაშლა', destructive: true,
    })
    if (!ok) return
    setError(''); setPending(true)
    try { await api(`/api/admin/${type}/${item.id}`, { method: 'DELETE' }); load(); toast('ჩანაწერი წაიშალა') } catch (e) { setError(e.message) }
    finally { setPending(false) }
  }
  const actions = item => <div className="row-actions">
    <button type="button" className="icon-button" disabled={pending} onClick={() => { setError(''); setEditing(item) }} aria-label="შეცვლა" title="შეცვლა"><Icon name="edit"/></button>
    <button type="button" className="icon-button danger" disabled={pending} onClick={() => remove(item)} aria-label="წაშლა" title="წაშლა"><Icon name="trash"/></button>
  </div>
  const config = type === 'plants'
    ? { cols: ['დასახელება', 'მისამართი', ''], rows: filteredItems.map(x => ({ key: x.id, cells: [<span className="cell-title"><span className="row-icon"><Icon name="plants" size={15}/></span>{x.name}</span>, x.address || <span className="muted">—</span>, actions(x)] })) }
    : { cols: ['ოპერატორი', 'ქარხანა', 'კლიენტი', ''], rows: filteredItems.map(x => ({ key: x.id, cells: [<strong className="operator-summary">{x.displayName}</strong>, x.plantName, <ClientDownload operator={x}/>, actions(x)] })) }
  const activeCount = [filters.name, type === 'plants' ? filters.address : filters.plantId].filter(Boolean).length
  return <div className="page">
    <FilterBar label={`${titles[type]} — ფილტრები`} activeCount={activeCount} actions={activeCount > 0 && <button className="button" type="button" onClick={() => setFilters({ name: '', address: '', plantId: '' })}>გასუფთავება</button>}>
      <Field label={type === 'plants' ? 'დასახელება' : 'სახელი'} className="grow">
        <SearchInput data-page-search value={filters.name} onChange={event => setFilters({ ...filters, name: event.target.value })} placeholder="ძიება"/>
      </Field>
      {type === 'plants'
        ? <Field label="მისამართი" className="grow"><SearchInput value={filters.address} onChange={event => setFilters({ ...filters, address: event.target.value })} placeholder="ძიება"/></Field>
        : <Field label="ქარხანა"><Select label="ქარხანა" value={filters.plantId} onChange={plantId => setFilters({ ...filters, plantId })} options={[{ value: '', label: 'ყველა' }, ...references.plants.map(item => ({ value: item.id, label: item.name }))]}/></Field>}
    </FilterBar>
    {error && !editing && <p className="banner error" role="alert"><Icon name="alert"/><span>{error}</span><button className="button small" onClick={load}>ხელახლა ცდა</button></p>}
    <section className="card table-card">
      <CardHeader title={titles[type]} note={filteredItems.length === items.length ? `${items.length} ჩანაწერი` : `${filteredItems.length} / ${items.length} ჩანაწერი`}>
        <button className="button primary" type="button" disabled={pending || !canCreate} onClick={openCreate}><Icon name="plus" size={14} strokeWidth={2.2}/>შექმნა</button>
      </CardHeader>
      {type === 'operators' && !references.plants.length && <p className="banner info inset"><Icon name="alert"/>ჯერ დაამატეთ ქარხანა.</p>}
      <DataTable className={`${type}-table`} columns={config.cols} rows={config.rows}
        empty={items.length
          ? <Empty icon="search" title="შედეგი ვერ მოიძებნა"/>
          : <Empty icon={type} title={type === 'plants' ? 'ქარხნები ჯერ არ არის' : 'ოპერატორები ჯერ არ არის'} action={canCreate && <button className="button primary" type="button" onClick={openCreate}><Icon name="plus" size={14} strokeWidth={2.2}/>შექმნა</button>}/>}/>
    </section>
    {editing && <DirectoryEditor type={type} value={editing} references={references} error={error} pending={pending} onClose={() => { if (!pending) setEditing(null) }} onSave={save}/>}
  </div>
}

function ClientDownload({ operator }) {
  const { toast } = useUI()
  const [state, setState] = useState({ busy: false, error: '' })
  const download = async () => {
    setState({ busy: true, error: '' })
    try {
      const response = await fetch('/downloads/FactoryService.Setup.exe', { cache: 'no-store' })
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
      const blob = new Blob([executable, config, marker, length], { type: 'application/vnd.microsoft.portable-executable' })
      const href = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = href
      link.download = `FactoryService-${safeFileName(operator.displayName)}-Setup.exe`
      document.body.appendChild(link); link.click(); link.remove()
      setTimeout(() => URL.revokeObjectURL(href), 1000)
      setState({ busy: false, error: '' })
      toast('ინსტალერის ჩამოტვირთვა დაიწყო')
    } catch (error) { setState({ busy: false, error: error.message }) }
  }
  return <div className="client-download">
    <button type="button" className="button tinted" disabled={state.busy} onClick={download}>{state.busy ? <Spinner/> : <Icon name="download" size={14}/>}{state.busy ? 'მზადდება…' : 'EXE-ის გადმოწერა'}</button>
    {state.error && <small role="alert">{state.error}</small>}
  </div>
}

function DirectoryEditor({ type, value, references, error, pending, onClose, onSave }) {
  const [form, setForm] = useState(value)
  const set = (key, next) => setForm(current => ({ ...current, [key]: next }))
  const submit = event => { event.preventDefault(); onSave(form) }
  const title = type === 'plants' ? (value.id ? 'ქარხნის რედაქტირება' : 'ახალი ქარხანა') : (value.id ? 'ოპერატორის რედაქტირება' : 'ახალი ოპერატორი')
  return <Sheet title={title} onClose={onClose} onSubmit={submit}
    footer={<>
      <button type="button" className="button" onClick={onClose}>გაუქმება</button>
      <button className="button primary" disabled={pending}>{pending ? <><Spinner/>ინახება…</> : value.id ? 'შენახვა' : 'შექმნა'}</button>
    </>}>
    {type === 'plants' && <div className="form-grid">
      <Field label="დასახელება" className="wide"><input autoFocus required value={form.name || ''} onChange={e => set('name', e.target.value)}/></Field>
      <Field label="მისამართი" className="wide"><input value={form.address || ''} onChange={e => set('address', e.target.value)}/></Field>
      <Field label="დროის სარტყელი" className="wide"><input value={form.timeZone || ''} onChange={e => set('timeZone', e.target.value)}/></Field>
    </div>}
    {type === 'operators' && <div className="form-grid">
      <Field label="სახელი და გვარი" className="wide"><input autoFocus required value={form.displayName || ''} onChange={e => set('displayName', e.target.value)}/></Field>
      <Field label="ქარხანა" className="wide"><Select label="ქარხანა" value={form.plantId || ''} onChange={plantId => set('plantId', plantId)} options={references.plants.map(x => ({ value: x.id, label: x.name }))}/></Field>
    </div>}
    {error && <p className="form-error" role="alert"><Icon name="alert" size={14}/>{error}</p>}
  </Sheet>
}

function DataTable({ columns, rows, empty, className = '' }) {
  return <div className={`table-scroll ${className}`}>
    <table>
      <thead><tr>{columns.map((column, index) => <th key={index} scope="col">{column}</th>)}</tr></thead>
      <tbody>{rows.length
        ? rows.map(row => <tr key={row.key}>{row.cells.map((cell, index) => <td key={index} data-label={columns[index] || undefined}>{cell}</td>)}</tr>)
        : <tr className="empty-row"><td colSpan={columns.length}>{empty || <Empty icon="search" title="მონაცემები არ მოიძებნა"/>}</td></tr>}</tbody>
    </table>
  </div>
}

function Loading() { return <div className="loading" role="status"><Spinner large/><p>მონაცემები იტვირთება…</p></div> }
function Empty({ icon = 'search', title, detail, action }) { return <div className="empty"><span className="empty-icon"><Icon name={icon} size={22}/></span><h3>{title}</h3>{detail && <p>{detail}</p>}{action}</div> }

/* ───────────────────────── Helpers ───────────────────────── */

function scoreBucket(value) { const numeric = Number(value); return Number.isFinite(numeric) && numeric > 0 ? Math.max(1, Math.min(5, Math.round(numeric))) : undefined }
function formatDate(value) { return new Intl.DateTimeFormat('ka-GE', { dateStyle: 'long' }).format(new Date(value)) }
function formatAxisDate(value) { return new Intl.DateTimeFormat('ka-GE', { day: '2-digit', month: '2-digit' }).format(new Date(value)) }
function formatDateTime(value) { return new Intl.DateTimeFormat('ka-GE', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
function formatNumber(value) { return new Intl.NumberFormat('ka-GE').format(value) }
function isoDate(offset) { const date = new Date(); date.setDate(date.getDate() + offset); return date.toISOString().slice(0, 10) }
function responseFilterDefaults() { return { from: isoDate(-30), to: isoDate(0), plantId: '', operatorId: '', score: '' } }
function queryParams(values) { return new URLSearchParams(Object.entries(values).filter(([, value]) => value !== '' && value != null)).toString() }
function matchesText(value, search) { return !search.trim() || String(value || '').toLocaleLowerCase('ka-GE').includes(search.trim().toLocaleLowerCase('ka-GE')) }
function roleName(value) { return ({ SystemAdministrator: 'სისტემის ადმინისტრატორი', PlantAdministrator: 'ქარხნის ადმინისტრატორი', ReportViewer: 'რეპორტების ნახვა', Auditor: 'აუდიტორი' })[value] || value }
function safeFileName(value) { return String(value || 'Operator').normalize('NFKD').replace(/[^\p{L}\p{N}.-]+/gu, '-').replace(/^-+|-+$/g, '').slice(0, 60) || 'Operator' }

createRoot(document.getElementById('root')).render(<React.StrictMode><UIProvider><App /></UIProvider></React.StrictMode>)
