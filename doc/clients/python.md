# Python client

The Python client package is published as [`nexus-api`](https://pypi.org/project/nexus-api/).

```bash
python -m pip install nexus-api
```

The package exposes synchronous and asynchronous clients. Use the synchronous client for notebooks and small scripts. Use the asynchronous client when integrating Nexus access into an async application or when coordinating multiple concurrent requests.

## Loading data

The sample notebooks show the recommended usage patterns:

- [Synchronous load notebook](https://github.com/nexus-main/nexus/blob/dev/samples/python/sample_load.ipynb)
- [Asynchronous load notebook](https://github.com/nexus-main/nexus/blob/dev/samples/python/sample_load_async.ipynb)

Typical steps are:

1. Create an HTTP client with the Nexus base URL.
2. Add a personal access token if authentication is required.
3. Create a `NexusClient` or `NexusAsyncClient`.
4. Request one or more resource paths for a time range.

## Exporting data

Exports are useful when another tool expects files instead of streamed arrays. The sample notebooks show how to create an export job, wait for completion, download the resulting ZIP file, and extract it locally:

- [Synchronous export notebook](https://github.com/nexus-main/nexus/blob/dev/samples/python/sample_export.ipynb)
- [Asynchronous export notebook](https://github.com/nexus-main/nexus/blob/dev/samples/python/sample_export_async.ipynb)

## Practical notes

- Use UTC timestamps when constructing begin and end dates.
- Prefer bounded time ranges instead of requesting complete long-running campaigns.
- Request only the resources needed for the analysis.
- Use the server visualization path in the UI for first inspection before downloading full-resolution data.
