export async function requestError(response: Response): Promise<Error> {
  if (isGitHubPagesMissingApi(response)) {
    return new Error(
      "This static GitHub Pages build does not include a running Nexus API backend. " +
        "The frontend is hosted for deployment testing; run Nexus locally or deploy the server to use catalogs, visualization, and exports.",
    );
  }

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

function isGitHubPagesMissingApi(response: Response) {
  if (response.status !== 404) {
    return false;
  }

  const url = new URL(response.url);

  return url.hostname.endsWith("github.io") && url.pathname.startsWith("/api/");
}
