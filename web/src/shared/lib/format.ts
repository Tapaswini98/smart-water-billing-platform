/**
 * Formatting lives in one place so a volume never appears as "12.5" on one screen
 * and "12.500 m³" on another. Locale comes from the browser; the unit does not.
 */

const VOLUME_DECIMALS = 3

export function formatVolume(cubicMetres: number | undefined | null): string {
  if (cubicMetres === undefined || cubicMetres === null) return '—'
  return `${cubicMetres.toLocaleString(undefined, {
    minimumFractionDigits: VOLUME_DECIMALS,
    maximumFractionDigits: VOLUME_DECIMALS,
  })} m³`
}

export function formatFlow(cubicMetresPerHour: number | undefined | null): string {
  if (cubicMetresPerHour === undefined || cubicMetresPerHour === null) return '—'
  return `${cubicMetresPerHour.toLocaleString(undefined, {
    minimumFractionDigits: 3,
    maximumFractionDigits: 3,
  })} m³/h`
}

export function formatMoney(amount: number | undefined | null, currency = 'INR'): string {
  if (amount === undefined || amount === null) return '—'
  try {
    return amount.toLocaleString(undefined, { style: 'currency', currency })
  } catch {
    // An unrecognised ISO code must not blank out an invoice total.
    return `${currency} ${amount.toFixed(2)}`
  }
}

export function formatDateTime(iso: string | undefined | null): string {
  if (!iso) return '—'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}

export function formatDate(iso: string | undefined | null): string {
  if (!iso) return '—'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleDateString(undefined, { dateStyle: 'medium' })
}

/**
 * The API sends anomaly flags as a combined string like "TotalDecreased, FlowTotalMismatch"
 * (or "None"). Split it so each flag can be rendered as its own badge.
 */
export function parseFlags(flags: string | undefined | null): string[] {
  if (!flags || flags === 'None') return []
  return flags
    .split(',')
    .map((flag) => flag.trim())
    .filter(Boolean)
}

/** "TotalDecreased" -> "Total decreased" */
export function humanizeFlag(flag: string): string {
  const spaced = flag.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase()
}
