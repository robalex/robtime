/**
 * The standard DOM-only file-download trick: a temporary object URL + `<a download>` click. No
 * `file-saver` or similar — matches this codebase's "small hand-written helper" style (lib/problem.ts,
 * lib/dates.ts). First use of a binary API response anywhere in this app (see
 * payrollExportBatches/queries.ts's useDownloadPayrollExportBatch) — the object URL must go through
 * the DOM rather than a bare `<a href>` straight to the endpoint, since only the shared `api` client
 * carries the bearer token needed to fetch the file in the first place.
 */
export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}
