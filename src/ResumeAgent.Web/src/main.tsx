import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api } from './api';
import type { Application, Fact, Job, Knowledge, MatchResult, Profile, Status } from './types';
import './styles.css';

const emptyProfile: Profile = {
  name: '', headline: '', contact: '', location: '', education: '', experience: '', skills: '',
};
const emptyJob: Job = { company: '', title: '', description: '', aiProhibited: false };

function App() {
  const [knowledge, setKnowledge] = useState<Knowledge | null>(null);
  const [profile, setProfile] = useState<Profile>(emptyProfile);
  const [status, setStatus] = useState<Status | null>(null);
  const [tab, setTab] = useState('Evidence');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const [documentId, setDocumentId] = useState('');
  const [locator, setLocator] = useState('');
  const [statement, setStatement] = useState('');
  const [approved, setApproved] = useState(false);
  const [job, setJob] = useState<Job>(emptyJob);
  const [match, setMatch] = useState<MatchResult | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [application, setApplication] = useState<Application | null>(null);
  const [confirmed, setConfirmed] = useState(false);

  async function refresh() {
    const data = await api<Knowledge>('/knowledge');
    setKnowledge(data);
    setProfile(data.profile);
  }

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError('');
    setNotice('');
    try { await action(); }
    catch (e) { setError(e instanceof Error ? e.message : 'The operation failed.'); }
    finally { setBusy(false); }
  }

  useEffect(() => {
    void run(async () => {
      await refresh();
      setStatus(await api<Status>('/status'));
    });
  }, []);

  const document = knowledge?.documents.find(d => d.id === documentId);
  const excerpt = document?.excerpts.find(e => e.locator === locator);
  const approvedCount = knowledge?.facts.filter(f => f.approved).length ?? 0;

  function changeJob(update: Partial<Job>) {
    setJob(current => ({ ...current, ...update }));
    setMatch(null);
    setSelected([]);
  }

  function loadApplication(value: Application) {
    setApplication(value);
    setConfirmed(false);
    setTab('Review');
  }

  async function updateFact(fact: Fact, approval: boolean) {
    await api(`/facts/${fact.id}`, 'PUT', { ...fact, approved: approval });
    setMatch(null);
    setSelected([]);
    await refresh();
  }

  return (
    <div className="shell">
      <header>
        <div>
          <p className="eyebrow">LOCAL-FIRST CAREER WORKSPACE</p>
          <h1>Resume Agent</h1>
          <p className="subtitle">Your evidence. A tailored application. A critical second look.</p>
        </div>
        <span className="privacy-badge">Local storage + local AI</span>
      </header>

      <aside className="privacy-note">
        Documents never go to GitHub or a cloud model. Review performance feedback locally and
        approve only accurate, non-confidential facts. AI output always needs your review.
      </aside>

      <div className="stats">
        <span><strong>{knowledge?.documents.length ?? 0}</strong> sources</span>
        <span><strong>{approvedCount}</strong> approved facts</span>
        <span><strong>{knowledge?.applications.length ?? 0}</strong> drafts</span>
        <span className={status?.ready ? 'success' : 'muted'}>
          {status?.ready ? 'Ollama ready' : 'Ollama not ready'} {status ? `- ${status.model}` : ''}
        </span>
        <button disabled={busy} onClick={() => void run(async () => {
          setStatus(await api<Status>('/status'));
        })}>Refresh AI status</button>
      </div>

      <nav aria-label="Workspace sections">
        {['Profile', 'Evidence', 'Apply', 'Review', 'History'].map(name =>
          <button key={name} disabled={busy} aria-current={tab === name ? 'page' : undefined}
            className={tab === name ? 'active' : ''} onClick={() => setTab(name)}>{name}</button>)}
      </nav>

      {busy && <p role="status" className="banner">Working locally. AI drafting and review can take several minutes.</p>}
      {error && <p role="alert" className="error banner">{error}</p>}
      {notice && <p role="status" className="success banner">{notice}</p>}

      {tab === 'Profile' && <section className="card">
        <h2>Verified profile</h2>
        <p>Enter only details you want in applications. These are treated as verified candidate facts.</p>
        <form onSubmit={event => {
          event.preventDefault();
          void run(async () => {
            await api('/profile', 'PUT', profile);
            await refresh();
            setNotice('Profile saved privately.');
          });
        }}>
          <div className="grid">
            {([
              ['name', 'Full name', 200], ['headline', 'Professional headline', 300],
              ['contact', 'Contact details / public links', 500], ['location', 'Location', 200],
            ] as const).map(([field, label, max]) => <label key={field}>{label}
              <input required={field === 'name'} maxLength={max} value={profile[field]}
                onChange={e => setProfile({ ...profile, [field]: e.target.value })} />
            </label>)}
          </div>
          <label>Work history: exact employers, titles, dates, and role context
            <textarea rows={7} maxLength={6000} value={profile.experience}
              onChange={e => setProfile({ ...profile, experience: e.target.value })} />
          </label>
          <label>Verified skills (not skills you merely want to learn)
            <textarea rows={3} maxLength={2000} value={profile.skills}
              onChange={e => setProfile({ ...profile, skills: e.target.value })} />
          </label>
          <label>Education and certifications: exact titles and dates
            <textarea rows={4} maxLength={3000} value={profile.education}
              onChange={e => setProfile({ ...profile, education: e.target.value })} />
          </label>
          <button className="primary" disabled={busy}>Save profile</button>
        </form>
      </section>}

      {tab === 'Evidence' && <>
        <section className="card">
          <h2>1. Import private sources</h2>
          <p>CVs, performance reviews, and project notes. PDF, DOCX, TXT, or Markdown; up to 10 MB per file.
            Scanned PDFs require local OCR. Raw source text stays on your computer.</p>
          <label className="upload">Choose documents
            <input type="file" multiple accept=".pdf,.docx,.txt,.md" disabled={busy}
              onChange={event => {
                const files = Array.from(event.target.files ?? []);
                event.target.value = '';
                void run(async () => {
                  let count = 0;
                  try {
                    for (const file of files) {
                      const form = new FormData();
                      form.append('file', file);
                      await api('/documents', 'POST', form);
                      count++;
                    }
                    setNotice(`Imported ${count} document(s) locally.`);
                  } finally {
                    await refresh();
                  }
                });
              }} />
          </label>
          <h2>2. Curate an evidence fact</h2>
          <p>Summarize a supported achievement in your own words. Include role, date, and scope where relevant.
            Remove internal codenames, other people's details, and confidential metrics before approval.</p>
          <div className="grid">
            <label>Source document
              <select value={documentId} onChange={e => {
                setDocumentId(e.target.value); setLocator(''); setApproved(false);
              }}>
                <option value="">Select a source</option>
                {knowledge?.documents.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
              </select>
            </label>
            <label>Source location
              <select value={locator} onChange={e => { setLocator(e.target.value); setApproved(false); }}>
                <option value="">Select a page or paragraph</option>
                {document?.excerpts.map(e => <option key={e.locator}>{e.locator}</option>)}
              </select>
            </label>
          </div>
          {excerpt && <details open><summary>Private source text</summary><pre>{excerpt.text}</pre></details>}
          <form onSubmit={event => {
            event.preventDefault();
            void run(async () => {
              await api('/facts', 'POST', { documentId, locator, statement, approved });
              setStatement(''); setApproved(false);
              await refresh();
              setNotice('Evidence saved. Only approved facts can be used by the agent.');
            });
          }}>
            <label>Supported, application-safe statement
              <textarea required rows={4} maxLength={3000} value={statement}
                onChange={e => { setStatement(e.target.value); setApproved(false); }} />
            </label>
            <label className="check"><input type="checkbox" checked={approved}
              onChange={e => setApproved(e.target.checked)} />
              I verified this against the source and removed confidential / third-party information.</label>
            <button className="primary" disabled={busy || !locator}>Save evidence</button>
          </form>
        </section>
        <section className="card">
          <h2>Evidence library</h2>
          {!knowledge?.facts.length && <p>No evidence facts yet. Import a source, then curate a fact above.</p>}
          {knowledge?.facts.map(f => <article key={f.id} className="fact">
            <p>{f.statement}</p>
            <small>{knowledge.documents.find(d => d.id === f.documentId)?.name} - {f.locator}</small>
            <label className="check"><input type="checkbox" checked={f.approved} disabled={busy}
              onChange={e => void run(() => updateFact(f, e.target.checked))} />Approved for applications</label>
          </article>)}
        </section>
      </>}

      {tab === 'Apply' && <section className="card">
        <h2>Tailor to a specific role</h2>
        <p>Paste a public job description. Matching uses approved facts only; scores are keyword overlaps,
          not a hiring probability. You choose which evidence the agent receives.</p>
        <div className="grid">
          <label>Company<input maxLength={200} value={job.company}
            onChange={e => changeJob({ company: e.target.value })} /></label>
          <label>Role title<input maxLength={200} value={job.title}
            onChange={e => changeJob({ title: e.target.value })} /></label>
        </div>
        <label>Job description<textarea rows={10} maxLength={24000} value={job.description}
          onChange={e => changeJob({ description: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={job.aiProhibited}
          onChange={e => changeJob({ aiProhibited: e.target.checked })} />
          This employer prohibits AI-generated application content.</label>
        <button disabled={busy || !approvedCount} onClick={() => void run(async () => {
          const result = await api<MatchResult>('/match', 'POST', job);
          setMatch(result);
          setSelected(result.matches.filter(m => m.score > 0).slice(0, 8).map(m => m.fact.id));
        })}>Find relevant evidence</button>
        {match?.aiProhibited && <p className="error banner">
          AI drafting is blocked for this role. Use your evidence for reference and write the application yourself.
        </p>}
        {match && <>
          <h3>Choose up to 12 facts ({selected.length} selected)</h3>
          {match.matches.map(m => <label className="fact check" key={m.fact.id}>
            <input type="checkbox" checked={selected.includes(m.fact.id)} disabled={busy}
              onChange={e => setSelected(e.target.checked
                ? [...selected, m.fact.id] : selected.filter(id => id !== m.fact.id))} />
            <span>{m.fact.statement}<small>{m.score} matching terms: {m.matchedTerms.join(', ') || 'none'}</small></span>
          </label>)}
          <button className="primary" disabled={busy || match.aiProhibited || !status?.ready
            || selected.length < 1 || selected.length > 12}
            onClick={() => void run(async () => {
              const draft = await api<Application>('/applications', 'POST', { job, factIds: selected });
              await refresh();
              loadApplication(draft);
            })}>Run reviewer agent: draft + review</button>
          {!status?.ready && <p>Start Ollama, install the configured model, and refresh AI status above.</p>}
        </>}
      </section>}

      {tab === 'Review' && <section className="card">
        {!application ? <p>Generate a draft or open one from History.</p> : <>
          <p className="eyebrow">DRAFT - VERIFY BEFORE USE</p>
          <h2>{application.job.title} - {application.job.company}</h2>
          <p>Model: {application.model}. Profile and evidence are snapshotted for this draft.</p>
          <h3>Reviewer findings</h3>
          {application.findings.map((f, index) =>
            <p key={index} className={f.severity === 'error' ? 'error banner' : 'banner'}>
              <strong>{f.severity.toUpperCase()}:</strong> {f.message}
            </p>)}
          <h3>CV preview</h3>
          <h4>{application.profile.name}</h4>
          <p>{application.profile.headline}</p>
          <p>{application.profile.contact} | {application.profile.location}</p>
          <h4>Professional summary</h4><p>{application.draft.summary}</p>
          <h4>Skills</h4><pre>{application.profile.skills}</pre>
          <h4>Work history</h4><pre>{application.profile.experience}</pre>
          <h4>Selected relevant achievements</h4>
          {application.draft.bullets.map((b, index) => <article className="fact" key={index}>
            <p>{b.text}</p>
            <details><summary>Supporting evidence</summary>
              {b.evidenceIds.map(id => <p key={id}>
                {application.evidence.find(f => f.id === id)?.statement ?? 'Unknown evidence ID'}
              </p>)}
            </details>
          </article>)}
          <h4>Education</h4><pre>{application.profile.education}</pre>
          <h3>Cover letter</h3><pre>{application.draft.coverLetter}</pre>
          <p>Edit exported DOCX files in your word processor before sending. If the reviewer flags errors,
            correct your evidence and regenerate; this app will not approve an error-bearing draft.</p>
          {!application.approvedAt && <>
            <label className="check"><input type="checkbox" checked={confirmed}
              onChange={e => setConfirmed(e.target.checked)} />
              I personally verified every claim, source, date, credential, and confidentiality concern.</label>
            <button className="primary" disabled={busy || !confirmed
              || application.findings.some(f => f.severity === 'error')}
              onClick={() => void run(async () => {
                const saved = await api<Application>(`/applications/${application.id}/approve`,
                  'POST', { confirmed });
                setApplication(saved);
                await refresh();
                setNotice('Approved locally. Review the final exported files before sending.');
              })}>Approve for export</button>
          </>}
          {application.approvedAt && <div className="exports">
            {['cv', 'cover-letter'].flatMap(kind => ['docx', 'md'].map(format =>
              <a key={`${kind}-${format}`} href={`/api/applications/${application.id}/export/${kind}/${format}`}
                download>{kind} .{format}</a>))}
          </div>}
        </>}
      </section>}

      {tab === 'History' && <section className="card">
        <h2>Private application history</h2>
        {!knowledge?.applications.length && <p>No applications drafted yet.</p>}
        {knowledge?.applications.slice().reverse().map(a => <article key={a.id} className="fact">
          <h3>{a.job.title} - {a.job.company}</h3>
          <p>{new Date(a.createdAt).toLocaleString()} - {a.approvedAt ? 'Approved for export' : 'Draft'}</p>
          <button onClick={() => loadApplication(a)}>Open review</button>
        </article>)}
      </section>}
      <footer>No auto-submission. No cloud inference. No invented accomplishments.</footer>
    </div>
  );
}

createRoot(document.getElementById('root')!).render(<App />);
