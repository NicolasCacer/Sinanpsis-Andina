import { QueryClientProvider } from '@tanstack/react-query'
import { queryClient } from './app/queryClient'
import { AuthProvider } from './app/auth'
import AppRouter from './app/router'

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider><AppRouter /></AuthProvider>
    </QueryClientProvider>
  )
}
