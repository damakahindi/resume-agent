# Resume Agent

A local-first reviewer agent that turns **verified career evidence** into tailored CV and cover-letter drafts. A .NET 10 API, React/TypeScript workspace, and **local Ollama** model keep your documents on your own computer.

This is a human-in-the-loop drafting tool, not an autonomous job applicant. It does not search for jobs, sign into accounts, contact employers, or submit applications.

## Plan and how to use it

1. **Build your private knowledge base.** Import CVs, annual performance reviews, and project notes as PDF, DOCX, TXT, or Markdown. Import is local text extraction, not an AI call. Scanned PDFs need local OCR first.
2. **Curate evidence.** Read the source excerpt and write an accurate, application-safe achievement. Each fact links to its source document and page/paragraph. Remove confidential names, internal metrics, and other people's personal details. Explicitly approve facts before the agent can use them.
3. **Maintain your baseline profile.** Enter your real work history, titles, dates, skills, education, and contact details. These fields are treated as verified facts and reused in each application.
4. **Paste a role.** Enter company, title, and the public job description. The matcher ranks approved facts by keyword overlap. Select up to 12 relevant facts; this is not an ATS score or hiring prediction.
5. **Call the reviewer agent.** It drafts a targeted summary, evidence-cited achievement bullets, and cover letter, then performs a separate critical review. Raw source text is excluded from both model calls.
6. **Personally verify and export.** Inspect the reviewer findings, source citations, all claims, and confidentiality. Drafts with reviewer errors cannot be approved. Fix evidence and regenerate. After explicit approval, export editable DOCX or Markdown files, make final edits, and submit yourself.

Employers may prohibit AI-written applications. Mark such roles explicitly; common prohibition wording is also detected. Drafting is blocked in that case. Detection is not comprehensive: check the employer's actual policy yourself.

## Requirements

- .NET 10 SDK.
- Node.js 22.12+ (or a compatible later LTS) and npm.
- PowerShell 7 for the optional import helper.
- [Ollama](https://ollama.com/download) and enough free memory/disk for your model.

Download and start a local model:

```powershell
ollama pull qwen2.5:7b
# If the Ollama desktop application is not already serving:
ollama serve
```

The model download is roughly 5 GB. CPU generation can take several minutes; hardware and model size matter. The app does not download models automatically.

## Run locally

From the repository root:

```powershell
dotnet restore ResumeAgent.sln
Push-Location src\ResumeAgent.Web
npm install
npm run build
Pop-Location
dotnet run --project src\ResumeAgent.Api --no-launch-profile
```

Open **http://127.0.0.1:5283**. The production frontend is served by .NET.

For frontend development, run the API as above and, in another terminal:

```powershell
Set-Location src\ResumeAgent.Web
npm run dev
```

Open http://127.0.0.1:5173. Vite proxies `/api` to the local backend.

Choose another **installed local** model before starting the API:

```powershell
$env:Ollama__Model = 'qwen2.5:7b'
$env:Ollama__Endpoint = 'http://127.0.0.1:11434/'
dotnet run --project src\ResumeAgent.Api --no-launch-profile
```

Only loopback model endpoints are accepted. HTTP redirects and HTTP proxies are disabled for inference.
Cloud model names and models reporting a remote host are rejected. Do not configure your local
Ollama service to relay requests to a cloud provider; the app cannot audit a modified local service.

### Import a private folder

Start the API first, then run:

```powershell
.\scripts\Import-Documents.ps1 -Folder 'C:\path\to\your\private-documents'
```

The helper imports supported files directly inside that folder, without recursive scanning or copying them into this repository. It does not approve facts or generate applications. Repeated imports create separate source entries.

## Privacy and publication boundary

**Public repository:** source code, dependency lockfiles, tests using synthetic data, a CI template, and this documentation.

**Private local storage:** extracted document text, your profile, approved/unapproved facts, pasted job descriptions, model drafts, reviewer findings, and application history. Stored at:

- Windows: `%LOCALAPPDATA%\ResumeAgent\knowledge.json`
- Other platforms: `.NET Environment.SpecialFolder.LocalApplicationData/ResumeAgent/knowledge.json`

Original uploaded files are parsed in memory; extracted text is persisted. Uploaded filenames are stored locally for source identification. No private career documents are bundled. There is no analytics or telemetry code in the app.

The private knowledge file is **not encrypted by the app**. Use an OS-protected account and encrypted disk; do not share the data directory or put it in a public/cloud-synced folder. Back it up privately. To remove local records, stop the API and delete the specific `knowledge.json` file; deleting it removes all imported text and application history.

Generated files go to your browser's download location. Keep them outside the source checkout. `.gitignore` is defense in depth, not a secret scanner: inspect staged content before every public push. Never commit profile data, annual reviews, CVs, generated letters, credentials, or internal employer material.

This is a **single-user, loopback-only** app without account authentication. It refuses non-local Host headers and requires a custom header for mutations, with restricted browser origins. Do not expose it through a tunnel, reverse proxy, LAN binding, or public deployment. Other software running as your OS user can still access local data.

## Architecture

The architecture uses general separation-of-concerns patterns learned from enterprise .NET development. All implementation here is standalone; no proprietary project code, prompts, documents, packages, or configuration is copied.

```text
src/
  ResumeAgent.Api/
    Models/       Profile, source excerpts, evidence, and application records
    Services/     Local persistence, extraction, matching, DOCX/Markdown export
    Clients/      Ollama HTTP transport, local endpoints only
    Workflows/    Draft -> citation/metric checks -> independent model review
    Program.cs    Thin local API endpoints and dependency composition
  ResumeAgent.Web/
    src/          React workspace, API client, typed contracts
tests/
  ResumeAgent.Tests/  Synthetic tests for privacy boundaries and workflow behavior
scripts/
  Import-Documents.ps1
```

The workflow owns orchestration; the client owns transport; services own extraction and persistence; API routes dispatch. Model calls use JSON schemas, low temperature, and explicit instructions to treat job/evidence strings as untrusted data.

### What the reviewer can and cannot guarantee

- Only explicitly approved evidence is selected, and each draft snapshots its profile/evidence.
- Deterministic checks flag absent/unknown bullet citations and numbers not present in the candidate profile or evidence.
- A second model pass checks semantic support, exaggeration, placeholders, and confidentiality concerns.
- Prompt instructions and model review are **not a security boundary or proof of truth**. A valid citation may still be misleading. A number occurring in evidence does not establish that it supports a claim. Always inspect the whole application yourself.
- Keyword matching is deliberately transparent, not semantic/vector retrieval. It may miss synonyms.
- This MVP produces a single-column CV with baseline work history and selected relevant achievements, not employer-specific template replication or a guaranteed ATS pass.
- No automatic extraction of approved achievements: the user establishes the evidence boundary.

### Tests

```powershell
dotnet test ResumeAgent.sln
Push-Location src\ResumeAgent.Web
npm run lint
npm run build
Pop-Location
```

Tests use synthetic records and stubbed model responses, never real performance reviews. CI does not need Ollama, private documents, API keys, or GitHub inference credentials.

The checked-in `examples/github-actions-ci.yml` is an **inactive template**. To enable it,
authorize your GitHub CLI login for the `workflow` scope, place the template at
`.github/workflows/ci.yml`, and push that change. A source-only GitHub token cannot install
Actions workflows; this project does not attempt to bypass that restriction.

If your network cannot negotiate TLS with nuget.org, Microsoft's public mirror can restore dependencies:

```powershell
dotnet restore ResumeAgent.sln --source 'https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json'
dotnet test ResumeAgent.sln --no-restore
```

Do not disable TLS certificate validation to work around network errors.

## Ubuntu and free coding agents

The app also runs on Ubuntu. Install the .NET 10 SDK, Node.js 22.12+ with npm,
Git, and Ollama. From your project directory:

```bash
dotnet restore ResumeAgent.sln
cd src/ResumeAgent.Web
npm install
npm run build
cd ../..
dotnet run --project src/ResumeAgent.Api --no-launch-profile
```

Use VS Code Remote SSH from your desktop and forward port **5283** in the Ports panel.
Keep the API and Ollama bound to loopback. Forwarding through SSH is different from exposing
the services to your home network or the Internet.

For private/local coding, use the free Continue extension with a local Ollama model.
For optional cloud help, sign into GitHub Copilot Free separately. Free cloud usage is limited;
no paid subscription is required for this app. A coding-agent subscription is separate from
the resume reviewer's local inference.

Install language extensions in the **SSH remote** context when VS Code prompts. Extensions
and model configs installed on your desktop are not automatically installed on Ubuntu.
Use `AGENTS.md` for project coding rules. Keep private career documents outside the workspace
and do not add them as cloud-agent context. Model-backed local coding agents still need
permission checks and human review; a small local model is not a substitute for tests.

## Next milestones

1. Editable evidence facts and draft revisions with re-review.
2. Local OCR for scanned documents and finer source anchors.
3. Optional local semantic retrieval and a tested synonym dictionary.
4. Multiple CV layouts, application-status tracking, and private backups.

GitHub Models is not used: [GitHub documents its retirement on July 30, 2026](https://docs.github.com/en/github-models). Local Ollama was selected instead.
