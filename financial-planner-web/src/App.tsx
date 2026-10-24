import { Alert, Box, Container, CssBaseline, Stack, Typography } from '@mui/material';
import React from 'react';
import { healthUrl } from './api';
import './App.css';

const App = () => (
  <>
    <CssBaseline />
    <Container component="main" maxWidth="md">
      <Box className="app-shell">
        <Stack spacing={3}>
          <Typography component="h1" variant="h3">
            Financial Planner
          </Typography>
          <Typography color="text.secondary" variant="body1">
            Your secure financial planning workspace is being prepared.
          </Typography>
          <Alert severity="info">
            API liveness endpoint: <code>{healthUrl}</code>
          </Alert>
        </Stack>
      </Box>
    </Container>
  </>
);

export default App;
