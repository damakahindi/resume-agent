export async function api<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const isForm = body instanceof FormData;
  const response = await fetch('/api' + path, {
    method,
    headers: {
      'X-Resume-Agent': 'local',
      ...(!isForm && body !== undefined ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
  });
  if (!response.ok) {
    let message = `Request failed (${response.status}).`;
    try {
      const error = await response.json() as { error?: string };
      message = error.error || message;
    } catch { /* A server/proxy failure may not return JSON. */ }
    throw new Error(message);
  }
  return response.json() as Promise<T>;
}
