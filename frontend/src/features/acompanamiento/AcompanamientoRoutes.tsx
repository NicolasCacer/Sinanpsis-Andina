import { Route, Routes } from 'react-router'
import PlanMensual from './PlanMensual'
import Registrar from './Registrar'

export default function AcompanamientoRoutes() {
  return (
    <Routes>
      <Route index element={<PlanMensual />} />
      <Route path="registrar/:hitoId" element={<Registrar />} />
    </Routes>
  )
}
