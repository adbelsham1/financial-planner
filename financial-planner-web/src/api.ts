const apiBaseUrl = (import.meta.env.PUBLIC_API_BASE_URL ?? 'http://localhost:5050/api').replace(/\/$/, '');

export const healthUrl = `${apiBaseUrl}/health/live`;
