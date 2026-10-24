import {
  Alert,
  Box,
  Button,
  Container,
  CssBaseline,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import React, { FormEvent, useState } from 'react';
import { registerOwner } from './api';
import './App.css';

const App = () => {
  const [error, setError] = useState<string>();
  const [submitted, setSubmitted] = useState(false);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(undefined);
    const form = new FormData(event.currentTarget);

    try {
      await registerOwner({
        displayName: String(form.get('displayName')),
        email: String(form.get('email')),
        password: String(form.get('password')),
        workspaceName: String(form.get('workspaceName')),
      });
      setSubmitted(true);
    } catch {
      setError('We could not create your workspace. Please try again.');
    }
  };

  return (
    <>
      <CssBaseline />
      <Container component="main" maxWidth="sm">
        <Box className="app-shell">
          <Stack component="form" spacing={3} onSubmit={handleSubmit}>
            <Typography component="h1" variant="h3">
              Financial Planner
            </Typography>
            <Typography color="text.secondary">
              Create your secure financial planning workspace.
            </Typography>
            {submitted && <Alert severity="success">Check your email for the verification code.</Alert>}
            {error && <Alert severity="error">{error}</Alert>}
            <TextField required label="Your name" name="displayName" autoComplete="name" />
            <TextField required label="Email" name="email" type="email" autoComplete="email" />
            <TextField
              required
              label="Password"
              name="password"
              type="password"
              autoComplete="new-password"
              helperText="Use at least 14 characters."
            />
            <TextField required label="Workspace name" name="workspaceName" />
            <Button size="large" type="submit" variant="contained">
              Create workspace
            </Button>
          </Stack>
        </Box>
      </Container>
    </>
  );
};

export default App;
