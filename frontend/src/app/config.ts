export const config = {
  authority: import.meta.env.VITE_OIDC_AUTHORITY ?? 'http://localhost:8180/realms/sses',
  apiUrl: import.meta.env.VITE_API_URL ?? '/api/v1',
  demo: import.meta.env.VITE_DEMO === '1',
}
