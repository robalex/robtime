import createClient from 'openapi-fetch'
import type { paths } from './schema'

// The generated `paths` are at root (/clients, /employees, …); baseUrl prefixes every request with
// /api so calls go through the Vite dev proxy (which strips /api and forwards to the local API) and,
// in production, through CloudFront's /api/* → App Runner behaviour (DEPLOY_PLAN.md §2). Overridable
// via VITE_API_BASE_URL for anyone pointing the SPA at a non-proxied API.
//
// A `client.GET('/clients', { params })` is fully typed — params, body, and the response union all
// come from schema.d.ts (UI_PLAN.md §3). Auth (the Authorization: Bearer header) is attached by a
// middleware added in the auth slice, not here — this stays a plain typed transport.
export const api = createClient<paths>({
  baseUrl: import.meta.env.VITE_API_BASE_URL ?? '/api',
})
