import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from './app/auth'
import AppRouter from './app/router'

const qc = new QueryClient({ defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } } })
export default function App() {
  return (
    <QueryClientProvider client={qc}>
      <AuthProvider><AppRouter /></AuthProvider>
    </QueryClientProvider>
  )
}
