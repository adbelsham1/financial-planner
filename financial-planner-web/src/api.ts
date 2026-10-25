const apiBaseUrl = (import.meta.env.PUBLIC_API_BASE_URL ?? 'http://localhost:5050/api').replace(/\/$/, '');

export const healthUrl = `${apiBaseUrl}/health/live`;

export const registerOwner = async (payload: {
  displayName: string;
  email: string;
  password: string;
  workspaceName: string;
}) => {
  const response = await fetch(`${apiBaseUrl}/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error('Unable to create your workspace.');
  }
};

export const beginSignIn = async (email: string, password: string) => {
  const response = await fetch(`${apiBaseUrl}/auth/sign-in`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error('Unable to sign in.');
  }
};

export const completeSignIn = async (email: string, code: string) => {
  const response = await fetch(`${apiBaseUrl}/auth/sign-in/verify`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, code }),
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error('The verification code is invalid or expired.');
  }
};

const post = async (path: string, payload: object) => {
  const response = await fetch(`${apiBaseUrl}/auth/${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
    credentials: 'include',
  });
  if (!response.ok) throw new Error(`Authentication request failed: ${response.status}`);
};

export const verifyEmail = (email: string, code: string) => post('verify-email', { email, code });
export const requestPasswordReset = (email: string) => post('password-reset', { email });
export const resendEmailVerification = (email: string) => post('verify-email/resend', { email });
export const resendSignInCode = (email: string) => post('sign-in/resend', { email });
export const completePasswordReset = (email: string, code: string, newPassword: string) =>
  post('password-reset/verify', { email, code, newPassword });
export const signOut = () => post('sign-out', {});
export const currentUser = async (): Promise<{ tenantId: string; userId: string; role: string }> => {
  const response = await fetch(`${apiBaseUrl}/auth/me`, { credentials: 'include' });
  if (!response.ok) throw new Error('Not authenticated.');
  return response.json() as Promise<{ tenantId: string; userId: string; role: string }>;
};

export type OwnershipParty = { name: string; percentage: number; isWorkspaceOwner: boolean };
export type LegalEntity = {
  id: string;
  name: string;
  kind: string;
  archivedAt: string | null;
  legalOwnershipPercentage: number;
  ownerAttributablePercentage: number;
  ownershipHistory: { id: string; supersedesOwnershipSnapshotId: string | null; effectiveFrom: string; parties: OwnershipParty[] }[];
  contributions: { id: string; reversesOwnershipContributionId: string | null; partyName: string; effectiveOn: string; amount: number; currency: string }[];
};

const entityRequest = async (path: string, method = 'GET', payload?: object) => {
  const response = await fetch(`${apiBaseUrl}/legal-entities${path}`, {
    method,
    headers: payload ? { 'Content-Type': 'application/json' } : undefined,
    body: payload ? JSON.stringify(payload) : undefined,
    credentials: 'include',
  });
  if (!response.ok) throw new Error(`Legal entity request failed: ${response.status}`);
  return response;
};

export const legalEntities = async () => (await entityRequest('/')).json() as Promise<LegalEntity[]>;
export const createLegalEntity = async (payload: {
  name: string; kind: string; effectiveFrom?: string; ownershipParties?: OwnershipParty[];
}) => (await entityRequest('/', 'POST', payload)).json() as Promise<LegalEntity>;
export const renameLegalEntity = async (id: string, name: string) => entityRequest(`/${id}`, 'PUT', { name });
export const archiveLegalEntity = async (id: string) => entityRequest(`/${id}/archive`, 'POST');
export const addOwnershipSnapshot = async (id: string, effectiveFrom: string, parties: OwnershipParty[], supersedesOwnershipSnapshotId?: string) =>
  entityRequest(`/${id}/ownership-snapshots`, 'POST', { effectiveFrom, parties, supersedesOwnershipSnapshotId });
export const addContribution = async (id: string, partyName: string, effectiveOn: string, amount: number) =>
  entityRequest(`/${id}/contributions`, 'POST', { partyName, effectiveOn, amount });
export const reverseContribution = async (id: string, contributionId: string, effectiveOn: string) =>
  entityRequest(`/${id}/contributions/${contributionId}/reverse`, 'POST', { effectiveOn });

export type LedgerAccount = { id: string; code: string; name: string; type: string; archivedAt: string | null };
export type LedgerJournal = {
  id: string; type: string; status: string; currency: string; effectiveOn: string; description: string;
  reversesJournalId: string | null; adjustsJournalId: string | null;
  lines: { id: string; accountId: string; accountCode: string; accountName: string; debit: number; credit: number; gstTaxTreatment: string; gstRate: number }[];
};
export type TrialBalance = {
  asOf: string; totalDebits: number; totalCredits: number;
  accounts: { accountId: string; code: string; name: string; type: string; debit: number; credit: number }[];
};

const ledgerRequest = async (entityId: string, path: string, method = 'GET', payload?: object) => {
  const response = await fetch(`${apiBaseUrl}/legal-entities/${entityId}/ledger${path}`, {
    method, headers: payload ? { 'Content-Type': 'application/json' } : undefined,
    body: payload ? JSON.stringify(payload) : undefined, credentials: 'include',
  });
  if (!response.ok) throw new Error(`Ledger request failed: ${response.status}`);
  return response;
};

export const ledgerAccounts = async (entityId: string) =>
  (await ledgerRequest(entityId, '/accounts')).json() as Promise<LedgerAccount[]>;
export const ledgerJournals = async (entityId: string) =>
  (await ledgerRequest(entityId, '/journals')).json() as Promise<LedgerJournal[]>;
export const createLedgerAccount = async (entityId: string, code: string, name: string, type: string) =>
  (await ledgerRequest(entityId, '/accounts', 'POST', { code, name, type })).json() as Promise<LedgerAccount>;
export const createLedgerJournal = async (entityId: string, payload: object) =>
  (await ledgerRequest(entityId, '/journals', 'POST', payload)).json() as Promise<LedgerJournal>;
export const createOpeningBalance = async (entityId: string, payload: object) =>
  (await ledgerRequest(entityId, '/opening-balances', 'POST', payload)).json() as Promise<LedgerJournal>;
export const approveLedgerJournal = async (entityId: string, journalId: string) =>
  (await ledgerRequest(entityId, `/journals/${journalId}/approve`, 'POST')).json() as Promise<LedgerJournal>;
export const reverseLedgerJournal = async (entityId: string, journalId: string, effectiveOn: string, description: string) =>
  (await ledgerRequest(entityId, `/journals/${journalId}/reverse`, 'POST', { effectiveOn, description, lines: [] })).json() as Promise<LedgerJournal>;
export const ledgerAudit = async (entityId: string, journalId: string) =>
  (await ledgerRequest(entityId, `/journals/${journalId}/audit`)).json() as Promise<{ action: string; occurredAt: string }[]>;
export const trialBalance = async (entityId: string, asOf: string) =>
  (await ledgerRequest(entityId, `/trial-balance?asOf=${encodeURIComponent(asOf)}`)).json() as Promise<TrialBalance>;
