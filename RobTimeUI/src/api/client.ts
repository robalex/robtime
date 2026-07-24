import createClient, { type Middleware } from 'openapi-fetch'
import { tokenHolder } from '@/auth/AuthProvider'
import type { paths } from './schema'

// The generated `paths` are at root (/clients, /employees, …); baseUrl prefixes every request with
// /api so calls go through the Vite dev proxy (which strips /api and forwards to the local API) and,
// in production, through CloudFront's /api/* → App Runner behaviour (DEPLOY_PLAN.md §2). Overridable
// via VITE_API_BASE_URL for anyone pointing the SPA at a non-proxied API.
//
// A `client.GET('/clients', { params })` is fully typed — params, body, and the response union all
// come from schema.d.ts (UI_PLAN.md §3).
export const api = createClient<paths>({
  baseUrl: import.meta.env.VITE_API_BASE_URL ?? '/api',
})

// Attaches the bearer token per request rather than baking it into the client at construction, so
// signing in or out doesn't require rebuilding the client (which would invalidate every cached
// query). Reads the module-level holder because middleware runs outside React.
const authMiddleware: Middleware = {
  onRequest({ request }) {
    const token = tokenHolder.current
    if (token) {
      request.headers.set('Authorization', `Bearer ${token}`)
    }
    return request
  },
}

api.use(authMiddleware)
