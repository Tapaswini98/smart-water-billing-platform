import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'

import { LoginPage } from '@/features/auth/LoginPage'
import { RequireAuth } from '@/features/auth/RequireAuth'
import { InvoicesPage } from '@/features/invoices/InvoicesPage'
import { MeterReadingsPage } from '@/features/meters/MeterReadingsPage'
import { MetersPage } from '@/features/meters/MetersPage'
import { PricingPage } from '@/features/pricing/PricingPage'

import { AppLayout } from './AppLayout'
import { Providers } from './Providers'

export function App() {
  return (
    <BrowserRouter>
      <Providers>
        <Routes>
          <Route path="/login" element={<LoginPage />} />

          <Route
            element={
              <RequireAuth>
                <AppLayout />
              </RequireAuth>
            }
          >
            <Route index element={<Navigate to="/meters" replace />} />
            <Route path="meters" element={<MetersPage />} />
            <Route path="meters/:meterId/readings" element={<MeterReadingsPage />} />
            <Route path="invoices" element={<InvoicesPage />} />
            <Route
              path="pricing"
              element={
                <RequireAuth role="Admin">
                  <PricingPage />
                </RequireAuth>
              }
            />
          </Route>

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </Providers>
    </BrowserRouter>
  )
}
