import { Alert, Box, Button, Container, CssBaseline, Stack, TextField, Typography } from '@mui/material';
import React, { FormEvent, useEffect, useState } from 'react';
import {
  beginSignIn, completePasswordReset, completeSignIn, currentUser, registerOwner, requestPasswordReset, resendEmailVerification,
  resendSignInCode, signOut, verifyEmail,
} from './api';
import './App.css';

type Screen = 'register' | 'verify-email' | 'sign-in' | 'sign-in-otp' | 'reset-request' | 'reset-verify' | 'authenticated';

const App = () => {
  const [screen, setScreen] = useState<Screen>('register');
  const [email, setEmail] = useState('');
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [user, setUser] = useState<{ tenantId: string; userId: string; role: string }>();

  useEffect(() => {
    currentUser().then((authenticatedUser) => {
      setUser(authenticatedUser);
      setScreen('authenticated');
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
        const authenticatedUser = await currentUser(); setUser(authenticatedUser); setScreen('authenticated');
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
    setUser(undefined); setScreen('sign-in'); setNotice('You have signed out.');
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
      {screen === 'authenticated' && <Stack spacing={2}>
        <Typography variant="h5">You are signed in</Typography>
        <Typography color="text.secondary">Workspace: {user?.tenantId} · Role: {user?.role}</Typography>
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
