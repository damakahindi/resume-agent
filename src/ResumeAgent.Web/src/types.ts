export interface Profile {
  name: string;
  headline: string;
  contact: string;
  location: string;
  education: string;
  experience: string;
  skills: string;
}

export interface SourceDocument {
  id: string;
  name: string;
  importedAt: string;
  excerpts: { locator: string; text: string }[];
}

export interface Fact {
  id: string;
  documentId: string;
  locator: string;
  statement: string;
  approved: boolean;
}

export interface Job {
  company: string;
  title: string;
  description: string;
  aiProhibited: boolean;
}

export interface Application {
  id: string;
  createdAt: string;
  job: Job;
  profile: Profile;
  evidence: Fact[];
  draft: {
    summary: string;
    bullets: { text: string; evidenceIds: string[] }[];
    coverLetter: string;
  };
  findings: { severity: string; message: string }[];
  model: string;
  approvedAt: string | null;
}

export interface Knowledge {
  profile: Profile;
  documents: SourceDocument[];
  facts: Fact[];
  applications: Application[];
}

export interface MatchResult {
  aiProhibited: boolean;
  matches: { fact: Fact; score: number; matchedTerms: string[] }[];
}

export interface Status {
  model: string;
  ready: boolean;
  installedModels: string[];
}
