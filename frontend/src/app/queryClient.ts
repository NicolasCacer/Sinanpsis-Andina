import { QueryClient } from '@tanstack/react-query'
// networkMode 'always': las consultas con respaldo local deben ejecutarse también sin red
export const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false, networkMode: 'always' }, mutations: { networkMode: 'always' } },
})
