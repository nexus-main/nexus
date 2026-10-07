export async function requestError(response: Response): Promise<Error> {
  const status = `Nexus request failed: ${response.status} ${response.statusText}`;
  let detail = "";

  try {
    detail = (await response.text()).trim();

    if (response.headers.get("content-type")?.includes("json") && detail) {
      const problem = JSON.parse(detail);

      detail = typeof problem === "string" ? problem : problem.detail || problem.title || detail;
    }
  } catch {
    // Preserve the HTTP status if the error body cannot be read or decoded.
  }

  return new Error(detail ? `${status}: ${detail}` : status);
}
