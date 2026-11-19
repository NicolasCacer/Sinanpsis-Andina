import { useAuth } from './auth'

export default function Landing() {
  const { ingresar, aviso } = useAuth()
  return (
    <main className="min-h-screen flex flex-col items-center justify-center gap-6 bg-gradient-to-b from-andino-900 to-andino-700 text-white p-6">
      <img src="/icon.svg" alt="" className="w-20 h-20" />
      <h1 className="text-4xl font-bold tracking-tight">SSES</h1>
      <p className="text-lg text-andino-50 text-center max-w-md">
        Sistema de Seguimiento de Actividades Extracurriculares
        <br />
        <span className="opacity-80 text-base">Sinapsis Andina S.A.S. — Tecnología que llega a cada rincón del país</span>
      </p>
      {aviso && <p role="alert" className="rounded bg-red-100 text-red-800 px-4 py-2 text-sm">{aviso}</p>}
      <button onClick={ingresar} className="rounded-lg bg-cumple px-10 py-3 text-lg font-semibold shadow hover:brightness-110 focus:outline-none focus:ring-4 focus:ring-white/40">
        Ingresar
      </button>
      <p className="text-xs text-andino-50/70 max-w-sm text-center">Acceso con segundo factor. Cada consulta o cambio queda registrado a tu nombre.</p>
    </main>
  )
}
