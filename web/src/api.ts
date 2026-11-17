export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'include',
    headers: {
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
      ...init.headers,
    },
  })

  if (!response.ok) {
    if (response.status === 401) throw new Error('Sessão encerrada. Entre novamente.')

    const body = await response.json().catch(() => null)
    const fieldErrors = body?.errors ? Object.values(body.errors).flat().join(' ') : ''
    throw new Error(fieldErrors || body?.title || 'Não foi possível concluir a operação.')
  }

  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}
