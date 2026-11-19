import { Route, Routes } from 'react-router'
import PlanMensual from './PlanMensual'
import Registrar from './Registrar'
import Conflicto from './Conflicto'

export default function AcompanamientoRoutes() {
  return (
    <Routes>
      <Route index element={<PlanMensual />} />
      <Route path="registrar/:hitoId" element={<Registrar />} />
      <Route path="conflicto/:clave" element={<Conflicto />} />
    </Routes>
  )
}
