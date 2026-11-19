export default function App() {
  return (
    <main className="min-h-screen flex flex-col items-center justify-center gap-6 bg-gradient-to-b from-andino-900 to-andino-700 text-white p-6">
      <img src="/icon.svg" alt="" className="w-20 h-20" />
      <h1 className="text-4xl font-bold tracking-tight">SSES</h1>
      <p className="text-lg text-andino-50 text-center max-w-md">
        Sistema de Seguimiento de Actividades Extracurriculares
        <br />
        <span className="opacity-80">Sinapsis Andina S.A.S. — Tecnología que llega a cada rincón del país</span>
      </p>
      <button className="rounded-lg bg-cumple px-8 py-3 text-lg font-semibold shadow hover:brightness-110">Ingresar</button>
    </main>
  )
}
