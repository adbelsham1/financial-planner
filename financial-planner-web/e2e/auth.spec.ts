import { expect, test, type APIRequestContext } from '@playwright/test';

const api = 'http://127.0.0.1:5050/api';

async function latestCode(request: APIRequestContext, email: string) {
  const response = await request.get(`${api}/auth/development/emails/${encodeURIComponent(email)}`);
  expect(response.ok()).toBeTruthy();
  return (await response.json()).code as string;
}

test('owner can register, activate, sign in, reset password, and sign out', async ({ page, request }) => {
  const email = `owner-${Date.now()}@example.test`;
  const password = 'A-long-enough-password1!';
  await page.goto('/');
  await page.getByLabel('Your name').fill('Taylor Owner');
  await page.getByLabel('Workspace name').fill('Taylor Workspace');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Create workspace' }).click();
  await expect(page.getByLabel('Verification code')).toBeVisible();
  await page.getByLabel('Verification code').fill(await latestCode(request, email));
  await page.getByRole('button', { name: 'Activate workspace' }).click();
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByLabel('Verification code')).toBeVisible();
  await page.getByLabel('Verification code').fill(await latestCode(request, email));
  await page.getByRole('button', { name: 'Verify and sign in' }).click();
  await expect(page.getByRole('heading', { name: 'You are signed in' })).toBeVisible();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await page.getByRole('button', { name: 'Forgot password?' }).click();
  await page.getByLabel('Email').fill(email);
  await page.getByRole('button', { name: 'Email reset code' }).click();
  await expect(page.getByLabel('Verification code')).toBeVisible();
  await page.getByLabel('Verification code').fill(await latestCode(request, email));
  await page.getByLabel('New password').fill('A-different-long-password1!');
  await page.getByRole('button', { name: 'Reset password' }).click();
  await expect(page.getByText('Your password has been reset. Sign in to continue.')).toBeVisible();
});

test('invalid OTP does not authenticate', async ({ page, request }) => {
  const email = `invalid-${Date.now()}@example.test`;
  const password = 'A-long-enough-password1!';
  await request.post(`${api}/auth/register`, { data: { displayName: 'Invalid Test', email, password, workspaceName: 'Invalid Workspace' } });
  await request.post(`${api}/auth/verify-email`, { data: { email, code: await latestCode(request, email) } });
  await page.goto('/');
  await page.getByRole('button', { name: 'Already have an account? Sign in' }).click();
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByLabel('Verification code')).toBeVisible();
  await page.getByLabel('Verification code').fill('000000');
  await page.getByRole('button', { name: 'Verify and sign in' }).click();
  await expect(page.getByText('We could not complete that request.')).toBeVisible();
});
