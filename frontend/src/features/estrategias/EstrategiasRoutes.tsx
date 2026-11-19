import { Route, Routes } from 'react-router'
import Listado from './Listado'
import Formulario from './Formulario'
import Ficha from './Ficha'
import Asociar from './Asociar'

export default function EstrategiasRoutes() {
  return (
    <Routes>
      <Route index element={<Listado />} />
      <Route path="nueva" element={<Formulario />} />
      <Route path=":id" element={<Ficha />} />
      <Route path=":id/editar" element={<Formulario />} />
      <Route path=":id/asociar" element={<Asociar />} />
    </Routes>
  )
}
