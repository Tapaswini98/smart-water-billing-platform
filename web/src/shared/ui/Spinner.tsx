export function Spinner({ label = 'Loading' }: { label?: string }) {
  return (
    <div className="spinner" role="status">
      <span className="spinner__ring" aria-hidden="true" />
      <span className="sr-only">{label}</span>
    </div>
  )
}
