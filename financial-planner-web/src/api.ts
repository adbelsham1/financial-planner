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
