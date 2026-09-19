import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/shared/ui/Button'
import { Card } from '@/shared/ui/Card'

/**
 * Interim screen. `GET /api/v1/meters` does not exist yet, so rather than invent a
 * list this asks for a meter id and navigates to the readings view — which is fully
 * wired. Replaced by a real table as soon as the endpoint lands.
 */
export function MetersPage() {
  const navigate = useNavigate()
  const [meterId, setMeterId] = useState('')

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (meterId.trim()) {
      void navigate(`/meters/${meterId.trim()}/readings`)
    }
  }

  return (
    <div className="stack">
      <Card
        title="Meters"
        description="The meter list endpoint is not built yet — open a meter by id in the meantime."
      >
        <form className="inline-form" onSubmit={handleSubmit}>
          <label className="field field--grow">
            <span className="field__label">Meter id</span>
            <input
              className="field__input"
              value={meterId}
              onChange={(e) => setMeterId(e.target.value)}
              placeholder="00000000-0000-0000-0000-0000000000e1"
              spellCheck={false}
            />
          </label>
          <Button type="submit">View readings</Button>
        </form>

        <p className="prose prose--muted">
          Seeded demo meters (Development):
        </p>
        <ul className="prose prose--muted">
          <li>
            <code>00000000-0000-0000-0000-0000000000e1</code> — WM-2024-0001, Asha Menon
          </li>
          <li>
            <code>00000000-0000-0000-0000-0000000000e3</code> — WM-2024-0003, Ravi Kulkarni
          </li>
        </ul>
      </Card>
    </div>
  )
}
