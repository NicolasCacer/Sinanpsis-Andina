import { Route, Routes } from 'react-router'
import { MiAcompanamiento, Semana } from './MiAcompanamiento'

export default function MiAcompanamientoRoutes() {
  return (
    <Routes>
      <Route index element={<MiAcompanamiento />} />
      <Route path="semana/:hitoId" element={<Semana />} />
    </Routes>
  )
}
