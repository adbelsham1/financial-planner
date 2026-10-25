import { Alert, Box, Button, Container, CssBaseline, Divider, MenuItem, Stack, TextField, Typography } from '@mui/material';
import React, { FormEvent, useEffect, useState } from 'react';
import {
  addContribution, addOwnershipSnapshot, archiveLegalEntity, beginSignIn, completePasswordReset, completeSignIn,
  createLegalEntity, currentUser, legalEntities, type LegalEntity, registerOwner, renameLegalEntity,
  requestPasswordReset, resendEmailVerification, resendSignInCode, reverseContribution, signOut, verifyEmail,
} from './api';
import './App.css';

type Screen = 'register' | 'verify-email' | 'sign-in' | 'sign-in-otp' | 'reset-request' | 'reset-verify' | 'authenticated';

const App = () => {
  const [screen, setScreen] = useState<Screen>('register');
  const [email, setEmail] = useState('');
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [user, setUser] = useState<{ tenantId: string; userId: string; role: string }>();
  const [entities, setEntities] = useState<LegalEntity[]>([]);
  const [entityKind, setEntityKind] = useState('Personal');
  const [selectedEntityId, setSelectedEntityId] = useState<string>();

  const loadEntities = async () => setEntities(await legalEntities());

  useEffect(() => {
    currentUser().then((authenticatedUser) => {
      setUser(authenticatedUser);
      setScreen('authenticated');
      return authenticatedUser.role === 'Owner' ? loadEntities() : undefined;
    }).catch(() => undefined);
  }, []);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(undefined);
    setNotice(undefined);
    const form = new FormData(event.currentTarget);
    const submittedEmail = String(form.get('email')).trim();
    try {
      if (screen === 'register') {
        await registerOwner({ displayName: String(form.get('displayName')), email: submittedEmail, password: String(form.get('password')), workspaceName: String(form.get('workspaceName')) });
        setEmail(submittedEmail); setScreen('verify-email'); setNotice('Check your email for the activation code.');
      } else if (screen === 'verify-email') {
        await verifyEmail(email, String(form.get('code')));
        setScreen('sign-in'); setNotice('Your email is verified. Sign in to continue.');
      } else if (screen === 'sign-in') {
        await beginSignIn(submittedEmail, String(form.get('password')));
        setEmail(submittedEmail); setScreen('sign-in-otp'); setNotice('Check your email for your sign-in code.');
      } else if (screen === 'sign-in-otp') {
        await completeSignIn(email, String(form.get('code')));
        const authenticatedUser = await currentUser();
        setUser(authenticatedUser);
        if (authenticatedUser.role === 'Owner') await loadEntities();
        setScreen('authenticated');
      } else if (screen === 'reset-request') {
        await requestPasswordReset(submittedEmail);
        setEmail(submittedEmail); setScreen('reset-verify'); setNotice('If the account exists, a reset code has been sent.');
      } else if (screen === 'reset-verify') {
        await completePasswordReset(email, String(form.get('code')), String(form.get('password')));
        setScreen('sign-in'); setNotice('Your password has been reset. Sign in to continue.');
      }
    } catch {
      setError('We could not complete that request. Check your details or code and try again.');
    }
  };

  const logout = async () => {
    await signOut();
    setUser(undefined); setEntities([]); setScreen('sign-in'); setNotice('You have signed out.');
  };

  const manageEntity = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(undefined); setNotice(undefined);
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    try {
      const kind = String(form.get('kind'));
      const payload = {
        name: String(form.get('entityName')),
        kind,
        ...(kind === 'Joint' ? {
          effectiveFrom: String(form.get('effectiveFrom')),
          ownershipParties: [
            { name: String(form.get('ownerPartyName')), percentage: Number(form.get('ownerPercentage')), isWorkspaceOwner: true },
            { name: String(form.get('coOwnerPartyName')), percentage: Number(form.get('coOwnerPercentage')), isWorkspaceOwner: false },
          ],
        } : {}),
      };
      const entity = await createLegalEntity(payload);
      setSelectedEntityId(entity.id); await loadEntities(); formElement.reset(); setEntityKind('Personal');
      setNotice('Legal entity created.');
    } catch {
      setError('We could not create that legal entity. Check the ownership allocation and try again.');
    }
  };

  const addHistory = async (event: FormEvent<HTMLFormElement>, entity: LegalEntity) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    try {
      const supersedesOwnershipSnapshotId = String(form.get('supersedesOwnershipSnapshotId') ?? '');
      await addOwnershipSnapshot(entity.id, String(form.get('effectiveFrom')), [
        { name: String(form.get('ownerPartyName')), percentage: Number(form.get('ownerPercentage')), isWorkspaceOwner: true },
        { name: String(form.get('coOwnerPartyName')), percentage: Number(form.get('coOwnerPercentage')), isWorkspaceOwner: false },
      ], supersedesOwnershipSnapshotId || undefined);
      await loadEntities(); setNotice('Ownership history added.');
    } catch {
      setError('We could not add that ownership history. Percentages must total 100%.');
    }
  };

  const recordContribution = async (event: FormEvent<HTMLFormElement>, entity: LegalEntity) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    try {
      await addContribution(entity.id, String(form.get('partyName')), String(form.get('effectiveOn')), Number(form.get('amount')));
      await loadEntities(); setNotice('Contribution recorded.');
    } catch {
      setError('We could not record that contribution. Select a current ownership party and enter a positive amount.');
    }
  };

  const renameEntity = async (event: FormEvent<HTMLFormElement>, entity: LegalEntity) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    try {
      await renameLegalEntity(entity.id, String(form.get('name')));
      await loadEntities(); setNotice('Legal entity renamed.');
    } catch {
      setError('We could not rename that legal entity.');
    }
  };

  const reverseRecordedContribution = async (event: FormEvent<HTMLFormElement>, entity: LegalEntity) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    try {
      await reverseContribution(entity.id, String(form.get('contributionId')), String(form.get('effectiveOn')));
      await loadEntities(); setNotice('Contribution reversed.');
    } catch {
      setError('We could not reverse that contribution.');
    }
  };

  const resendCode = async () => {
    try {
      if (screen === 'verify-email') await resendEmailVerification(email);
      else if (screen === 'sign-in-otp') await resendSignInCode(email);
      else return;
      setNotice('A new verification code has been sent.');
    } catch {
      setError('We could not send another code yet. Please try again shortly.');
    }
  };

  const isCodeScreen = screen === 'verify-email' || screen === 'sign-in-otp' || screen === 'reset-verify';
  return <><CssBaseline /><Container component="main" maxWidth="sm"><Box className="app-shell">
    <Stack spacing={3}>
      <Typography component="h1" variant="h3">Financial Planner</Typography>
      {notice && <Alert severity="success">{notice}</Alert>}
      {error && <Alert severity="error">{error}</Alert>}
      {screen === 'authenticated' && <Stack spacing={3}>
        <Typography variant="h5">You are signed in</Typography>
        <Typography color="text.secondary">Workspace: {user?.tenantId} · Role: {user?.role}</Typography>
        {user?.role === 'Owner' ? <><Divider />
        <Typography component="h2" variant="h5">Legal entities</Typography>
        <Stack component="form" spacing={2} onSubmit={manageEntity}>
          <TextField required label="Entity name" name="entityName" />
          <TextField required select label="Entity type" name="kind" value={entityKind} onChange={(event) => setEntityKind(event.target.value)}>
            <MenuItem value="Personal">Personal / household</MenuItem>
            <MenuItem value="Smsf">SMSF</MenuItem>
            <MenuItem value="Joint">Joint</MenuItem>
          </TextField>
          {entityKind === 'Joint' && <>
            <TextField required label="Ownership effective from" name="effectiveFrom" type="date" slotProps={{ inputLabel: { shrink: true } }} />
            <TextField required label="Workspace owner name" name="ownerPartyName" />
            <TextField required label="Workspace owner percentage" name="ownerPercentage" type="number" inputProps={{ min: 0.0001, max: 100, step: 0.0001 }} />
            <TextField required label="Co-owner name" name="coOwnerPartyName" />
            <TextField required label="Co-owner percentage" name="coOwnerPercentage" type="number" inputProps={{ min: 0.0001, max: 100, step: 0.0001 }} />
          </>}
          <Button type="submit" variant="contained">Create legal entity</Button>
        </Stack>
        {entities.map((entity) => <Stack key={entity.id} spacing={1} sx={{ border: 1, borderColor: 'divider', borderRadius: 1, p: 2 }}>
          <Typography component="h3" variant="h6">{entity.name} {entity.archivedAt ? '(Archived)' : ''}</Typography>
          <Typography color="text.secondary">{entity.kind} · Legal ownership: {entity.legalOwnershipPercentage}% · Your attributable share: {entity.ownerAttributablePercentage}%</Typography>
          <Stack direction="row" spacing={1}>
            <Button onClick={() => setSelectedEntityId(entity.id)}>View history</Button>
            {!entity.archivedAt && <Button color="warning" onClick={() => archiveLegalEntity(entity.id).then(loadEntities).catch(() => setError('We could not archive that entity.'))}>Archive</Button>}
          </Stack>
          {selectedEntityId === entity.id && <Stack spacing={2}>
            {!entity.archivedAt && <Stack component="form" direction="row" spacing={1} onSubmit={(event) => renameEntity(event, entity)}>
              <TextField required label="Entity name" name="name" defaultValue={entity.name} size="small" />
              <Button type="submit">Rename</Button>
            </Stack>}
            <Typography variant="subtitle1">Ownership history</Typography>
            {entity.ownershipHistory.map((snapshot) => <Typography key={snapshot.id}>{snapshot.effectiveFrom}: {snapshot.parties.map((party) => `${party.name}${party.isWorkspaceOwner ? ' (you)' : ''} ${party.percentage}%`).join(', ')}{snapshot.supersedesOwnershipSnapshotId ? ' (supersedes an earlier revision)' : ''}</Typography>)}
            {entity.kind === 'Joint' && !entity.archivedAt && <>
              <Stack component="form" spacing={1} onSubmit={(event) => addHistory(event, entity)}>
                <Typography variant="subtitle1">Add ownership snapshot</Typography>
                <TextField required label="Effective from" name="effectiveFrom" type="date" slotProps={{ inputLabel: { shrink: true } }} />
                <TextField select label="Supersede ownership snapshot" name="supersedesOwnershipSnapshotId" defaultValue="">
                  <MenuItem value="">New effective date</MenuItem>
                  {entity.ownershipHistory.map((snapshot) => <MenuItem key={snapshot.id} value={snapshot.id}>{snapshot.effectiveFrom}</MenuItem>)}
                </TextField>
                <TextField required label="Workspace owner name" name="ownerPartyName" />
                <TextField required label="Workspace owner percentage" name="ownerPercentage" type="number" inputProps={{ min: 0.0001, max: 100, step: 0.0001 }} />
                <TextField required label="Co-owner name" name="coOwnerPartyName" />
                <TextField required label="Co-owner percentage" name="coOwnerPercentage" type="number" inputProps={{ min: 0.0001, max: 100, step: 0.0001 }} />
                <Button type="submit">Add ownership snapshot</Button>
              </Stack>
              <Stack component="form" spacing={1} onSubmit={(event) => recordContribution(event, entity)}>
                <Typography variant="subtitle1">Record unequal contribution</Typography>
                <TextField required label="Party name" name="partyName" />
                <TextField required label="Contribution date" name="effectiveOn" type="date" slotProps={{ inputLabel: { shrink: true } }} />
                <TextField required label="Contribution amount (AUD)" name="amount" type="number" inputProps={{ min: 0.01, step: 0.01 }} />
                <Button type="submit">Record contribution</Button>
              </Stack>
              <Stack component="form" spacing={1} onSubmit={(event) => reverseRecordedContribution(event, entity)}>
                <Typography variant="subtitle1">Reverse contribution</Typography>
                <TextField required select label="Contribution" name="contributionId" defaultValue="">
                  {entity.contributions.filter((contribution) => contribution.amount > 0 && !entity.contributions.some((item) => item.reversesOwnershipContributionId === contribution.id)).map((contribution) =>
                    <MenuItem key={contribution.id} value={contribution.id}>{contribution.effectiveOn}: {contribution.partyName} AUD {contribution.amount}</MenuItem>)}
                </TextField>
                <TextField required label="Reversal date" name="effectiveOn" type="date" slotProps={{ inputLabel: { shrink: true } }} />
                <Button type="submit">Reverse contribution</Button>
              </Stack>
            </>}
            {entity.kind === 'Joint' && entity.contributions.map((contribution) => <Typography key={contribution.id}>{contribution.effectiveOn}: {contribution.partyName} contributed {contribution.currency} {contribution.amount}</Typography>)}
          </Stack>}
        </Stack>)}
        </> : <Typography color="text.secondary">Your role does not administer legal entities.</Typography>}
        <Button variant="outlined" onClick={logout}>Sign out</Button>
      </Stack>}
      {screen !== 'authenticated' && <Stack component="form" spacing={2} onSubmit={submit}>
        {screen === 'register' && <><Typography color="text.secondary">Create your secure financial planning workspace.</Typography>
          <TextField required label="Your name" name="displayName" autoComplete="name" />
          <TextField required label="Workspace name" name="workspaceName" />
        </>}
        {!isCodeScreen && <TextField required label="Email" name="email" type="email" autoComplete="email" defaultValue={email} />}
        {isCodeScreen && <TextField required label="Verification code" name="code" inputMode="numeric" autoComplete="one-time-code" />}
        {(screen === 'register' || screen === 'sign-in' || screen === 'reset-verify') && <TextField required label={screen === 'reset-verify' ? 'New password' : 'Password'} name="password" type="password" autoComplete={screen === 'register' || screen === 'reset-verify' ? 'new-password' : 'current-password'} helperText={screen === 'register' || screen === 'reset-verify' ? 'Use at least 14 characters.' : undefined} />}
        <Button size="large" type="submit" variant="contained">{screen === 'register' ? 'Create workspace' : screen === 'verify-email' ? 'Activate workspace' : screen === 'sign-in' ? 'Sign in' : screen === 'sign-in-otp' ? 'Verify and sign in' : screen === 'reset-request' ? 'Email reset code' : 'Reset password'}</Button>
      </Stack>}
      {screen === 'register' && <Button onClick={() => setScreen('sign-in')}>Already have an account? Sign in</Button>}
      {screen === 'sign-in' && <Stack direction="row" spacing={2}><Button onClick={() => setScreen('register')}>Create workspace</Button><Button onClick={() => setScreen('reset-request')}>Forgot password?</Button></Stack>}
      {(screen === 'verify-email' || screen === 'sign-in-otp') && <Stack direction="row" spacing={2}><Button onClick={resendCode}>Resend code</Button><Button onClick={() => setScreen('sign-in')}>Back to sign in</Button></Stack>}
      {screen === 'reset-request' && <Button onClick={() => setScreen('sign-in')}>Back to sign in</Button>}
    </Stack>
  </Box></Container></>;
};

export default App;
