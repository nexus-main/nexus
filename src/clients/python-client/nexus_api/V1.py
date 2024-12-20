# Python <= 3.9
from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import Enum
from typing import AsyncIterable, Awaitable, Iterable, Optional, TypeVar, Union
from urllib.parse import quote
from uuid import UUID

from httpx import Response

from ._encoder import JsonEncoder
from ._shared import (HttpRequestHandler, HttpRequestHandlerAsync,
                      _json_encoder_options, _to_string)

T = TypeVar("T")

class V1:
    """A client for version V1."""
    
    _artifacts: ArtifactsClient
    _catalogs: CatalogsClient
    _data: DataClient
    _jobs: JobsClient
    _packageReferences: PackageReferencesClient
    _sources: SourcesClient
    _system: SystemClient
    _users: UsersClient
    _writers: WritersClient


    def __init__(self, invoke: HttpRequestHandler):
        """
        Initializes a new instance of V1
        
            Args:
                client: The client to use.
        """

        self._artifacts = ArtifactsClient(invoke)
        self._catalogs = CatalogsClient(invoke)
        self._data = DataClient(invoke)
        self._jobs = JobsClient(invoke)
        self._packageReferences = PackageReferencesClient(invoke)
        self._sources = SourcesClient(invoke)
        self._system = SystemClient(invoke)
        self._users = UsersClient(invoke)
        self._writers = WritersClient(invoke)


    @property
    def artifacts(self) -> ArtifactsClient:
        """Gets the ArtifactsClient."""
        return self._artifacts

    @property
    def catalogs(self) -> CatalogsClient:
        """Gets the CatalogsClient."""
        return self._catalogs

    @property
    def data(self) -> DataClient:
        """Gets the DataClient."""
        return self._data

    @property
    def jobs(self) -> JobsClient:
        """Gets the JobsClient."""
        return self._jobs

    @property
    def package_references(self) -> PackageReferencesClient:
        """Gets the PackageReferencesClient."""
        return self._packageReferences

    @property
    def sources(self) -> SourcesClient:
        """Gets the SourcesClient."""
        return self._sources

    @property
    def system(self) -> SystemClient:
        """Gets the SystemClient."""
        return self._system

    @property
    def users(self) -> UsersClient:
        """Gets the UsersClient."""
        return self._users

    @property
    def writers(self) -> WritersClient:
        """Gets the WritersClient."""
        return self._writers



class ArtifactsClient:
    """Provides methods to interact with artifacts."""

    ___invoke: HttpRequestHandler
    
    def __init__(self, invoke: HttpRequestHandler):
        self.___invoke = invoke

    def download(self, artifact_id: str) -> Response:
        """
        Gets the specified artifact.

        Args:
            artifact_id: The artifact identifier.
        """

        __url = "/api/v1/artifacts/{artifactId}"
        __url = __url.replace("{artifactId}", quote(str(artifact_id), safe=""))

        return self.___invoke(Response, "GET", __url, "application/octet-stream", None, None)


class CatalogsClient:
    """Provides methods to interact with catalogs."""

    ___invoke: HttpRequestHandler
    
    def __init__(self, invoke: HttpRequestHandler):
        self.___invoke = invoke

    def search_catalog_items(self, resource_paths: list[str]) -> dict[str, CatalogItem]:
        """
        Searches for the given resource paths and returns the corresponding catalog items.

        Args:
        """

        __url = "/api/v1/catalogs/search-items"

        return self.___invoke(dict[str, CatalogItem], "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(resource_paths, _json_encoder_options)))

    def get(self, catalog_id: str) -> ResourceCatalog:
        """
        Gets the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(ResourceCatalog, "GET", __url, "application/json", None, None)

    def get_child_catalog_infos(self, catalog_id: str) -> list[CatalogInfo]:
        """
        Gets a list of child catalog info for the provided parent catalog identifier.

        Args:
            catalog_id: The parent catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/child-catalog-infos"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(list[CatalogInfo], "GET", __url, "application/json", None, None)

    def get_time_range(self, catalog_id: str) -> CatalogTimeRange:
        """
        Gets the specified catalog's time range.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/timerange"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(CatalogTimeRange, "GET", __url, "application/json", None, None)

    def get_availability(self, catalog_id: str, begin: datetime, end: datetime, step: timedelta) -> CatalogAvailability:
        """
        Gets the specified catalog's availability.

        Args:
            catalog_id: The catalog identifier.
            begin: Start date/time.
            end: End date/time.
            step: Step period.
        """

        __url = "/api/v1/catalogs/{catalogId}/availability"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        __query_values: dict[str, str] = {}

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query_values["step"] = quote(_to_string(step), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(CatalogAvailability, "GET", __url, "application/json", None, None)

    def get_license(self, catalog_id: str) -> Optional[str]:
        """
        Gets the license of the catalog if available.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/license"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(str, "GET", __url, "application/json", None, None)

    def get_attachments(self, catalog_id: str) -> list[str]:
        """
        Gets all attachments for the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(list[str], "GET", __url, "application/json", None, None)

    def upload_attachment(self, catalog_id: str, attachment_id: str, content: Union[bytes, Iterable[bytes], AsyncIterable[bytes]]) -> Response:
        """
        Uploads the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___invoke(Response, "PUT", __url, "application/octet-stream", "application/octet-stream", content)

    def delete_attachment(self, catalog_id: str, attachment_id: str) -> Response:
        """
        Deletes the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_attachment_stream(self, catalog_id: str, attachment_id: str) -> Response:
        """
        Gets the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___invoke(Response, "GET", __url, "application/octet-stream", None, None)

    def get_metadata(self, catalog_id: str) -> CatalogMetadata:
        """
        Gets the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/metadata"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(CatalogMetadata, "GET", __url, "application/json", None, None)

    def set_metadata(self, catalog_id: str, metadata: CatalogMetadata) -> Response:
        """
        Puts the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/metadata"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(Response, "PUT", __url, "application/octet-stream", "application/json", json.dumps(JsonEncoder.encode(metadata, _json_encoder_options)))


class DataClient:
    """Provides methods to interact with data."""

    ___invoke: HttpRequestHandler
    
    def __init__(self, invoke: HttpRequestHandler):
        self.___invoke = invoke

    def get_stream(self, resource_path: str, begin: datetime, end: datetime) -> Response:
        """
        Gets the requested data.

        Args:
            resource_path: The path to the resource data to stream.
            begin: Start date/time.
            end: End date/time.
        """

        __url = "/api/v1/data"

        __query_values: dict[str, str] = {}

        __query_values["resourcePath"] = quote(_to_string(resource_path), safe="")

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Response, "GET", __url, "application/octet-stream", None, None)


class JobsClient:
    """Provides methods to interact with jobs."""

    ___invoke: HttpRequestHandler
    
    def __init__(self, invoke: HttpRequestHandler):
        self.___invoke = invoke

    def get_jobs(self) -> list[Job]:
        """
        Gets a list of jobs.

        Args:
        """

        __url = "/api/v1/jobs"

        return self.___invoke(list[Job], "GET", __url, "application/json", None, None)

    def cancel_job(self, job_id: UUID) -> Response:
        """
        Cancels the specified job.

        Args:
            job_id: 
        """

        __url = "/api/v1/jobs/{jobId}"
        __url = __url.replace("{jobId}", quote(str(job_id), safe=""))

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_job_status(self, job_id: UUID) -> JobStatus:
        """
        Gets the status of the specified job.

        Args:
            job_id: 
        """

        __url = "/api/v1/jobs/{jobId}/status"
        __url = __url.replace("{jobId}", quote(str(job_id), safe=""))

        return self.___invoke(JobStatus, "GET", __url, "application/json", None, None)

    def export(self, parameters: ExportParameters) -> Job:
        """
        Creates a new export job.

        Args:
        """

        __url = "/api/v1/jobs/export"

        return self.___invoke(Job, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(parameters, _json_encoder_options)))

    def refresh_database(self) -> Job:
        """
        Creates a new job which reloads all extensions and resets the resource catalog.

        Args:
        """

        __url = "/api/v1/jobs/refresh-database"

        return self.___invoke(Job, "POST", __url, "application/json", None, None)

    def clear_cache(self, catalog_id: str, begin: datetime, end: datetime) -> Job:
        """
        Clears the aggregation data cache for the specified period of time.

        Args:
            catalog_id: The catalog identifier.
            begin: Start date/time.
            end: End date/time.
        """

        __url = "/api/v1/jobs/clear-cache"

        __query_values: dict[str, str] = {}

        __query_values["catalogId"] = quote(_to_string(catalog_id), safe="")

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Job, "POST", __url, "application/json", None, None)


class PackageReferencesClient:
    """Provides methods to interact with package references."""

    ___invoke: HttpRequestHandler
    
    def __init__(self, invoke: HttpRequestHandler):
        self.___invoke = invoke

    def get(self) -> dict[str, PackageReference]:
        """
        Gets the list of package references.

        Args:
        """

        __url = "/api/v1/packagereferences"

        return self.___invoke(dict[str, PackageReference], "GET", __url, "application/json", None, None)

    def create(self, package_reference: PackageReference) -> UUID:
        """
        Creates a package reference.

        Args:
        """

        __url = "/api/v1/packagereferences"

        return self.___invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(package_reference, _json_encoder_options)))

    def delete(self, id: UUID) -> None:
        """
        Deletes a package reference.

        Args:
            id: The ID of the package reference.
        """

        __url = "/api/v1/packagereferences/{id}"
        __url = __url.replace("{id}", quote(str(id), safe=""))

        return self.___invoke(type(None), "DELETE", __url, None, None, None)

    def get_versions(self, id: UUID) -> list[str]:
        """
        Gets package versions.

        Args:
            id: The ID of the package reference.
        """

        __url = "/api/v1/packagereferences/{id}/versions"
        __url = __url.replace("{id}", quote(str(id), safe=""))

        return self.___invoke(list[str], "GET", __url, "application/json", None, None)


class SourcesClient:
    """Provides methods to interact with sources."""

    ___invoke: HttpRequestHandler
    
    def __init__(self, invoke: HttpRequestHandler):
        self.___invoke = invoke

    def get_descriptions(self) -> list[ExtensionDescription]:
        """
        Gets the list of source descriptions.

        Args:
        """

        __url = "/api/v1/sources/descriptions"

        return self.___invoke(list[ExtensionDescription], "GET", __url, "application/json", None, None)

    def get_pipelines(self, user_id: Optional[str] = None) -> dict[str, DataSourcePipeline]:
        """
        Gets the list of data source pipelines.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/pipelines"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(dict[str, DataSourcePipeline], "GET", __url, "application/json", None, None)

    def create_pipeline(self, pipeline: DataSourcePipeline, user_id: Optional[str] = None) -> UUID:
        """
        Creates a data source pipeline.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/pipelines"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(pipeline, _json_encoder_options)))

    def delete_pipeline(self, pipeline_id: UUID, user_id: Optional[str] = None) -> Response:
        """
        Deletes a data source pipeline.

        Args:
            pipeline_id: The identifier of the pipeline.
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/pipelines/{pipelineId}"
        __url = __url.replace("{pipelineId}", quote(str(pipeline_id), safe=""))

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)


class SystemClient:
    """Provides methods to interact with system."""

    ___invoke: HttpRequestHandler
    
    def __init__(self, invoke: HttpRequestHandler):
        self.___invoke = invoke

    def get_default_file_type(self) -> str:
        """
        Gets the default file type.

        Args:
        """

        __url = "/api/v1/system/file-type"

        return self.___invoke(str, "GET", __url, "application/json", None, None)

    def get_help_link(self) -> str:
        """
        Gets the configured help link.

        Args:
        """

        __url = "/api/v1/system/help-link"

        return self.___invoke(str, "GET", __url, "application/json", None, None)

    def get_configuration(self) -> Optional[dict[str, object]]:
        """
        Gets the system configuration.

        Args:
        """

        __url = "/api/v1/system/configuration"

        return self.___invoke(dict[str, object], "GET", __url, "application/json", None, None)

    def set_configuration(self, configuration: Optional[dict[str, object]]) -> None:
        """
        Sets the system configuration.

        Args:
        """

        __url = "/api/v1/system/configuration"

        return self.___invoke(type(None), "PUT", __url, None, "application/json", json.dumps(JsonEncoder.encode(configuration, _json_encoder_options)))


class UsersClient:
    """Provides methods to interact with users."""

    ___invoke: HttpRequestHandler
    
    def __init__(self, invoke: HttpRequestHandler):
        self.___invoke = invoke

    def authenticate(self, scheme: str, return_url: str) -> Response:
        """
        Authenticates the user.

        Args:
            scheme: The authentication scheme to challenge.
            return_url: The URL to return after successful authentication.
        """

        __url = "/api/v1/users/authenticate"

        __query_values: dict[str, str] = {}

        __query_values["scheme"] = quote(_to_string(scheme), safe="")

        __query_values["returnUrl"] = quote(_to_string(return_url), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Response, "POST", __url, "application/octet-stream", None, None)

    def sign_out(self, return_url: str) -> None:
        """
        Logs out the user.

        Args:
            return_url: The URL to return after logout.
        """

        __url = "/api/v1/users/signout"

        __query_values: dict[str, str] = {}

        __query_values["returnUrl"] = quote(_to_string(return_url), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(type(None), "POST", __url, None, None, None)

    def delete_token_by_value(self, value: str) -> Response:
        """
        Deletes a personal access token.

        Args:
            value: The personal access token to delete.
        """

        __url = "/api/v1/users/tokens/delete"

        __query_values: dict[str, str] = {}

        __query_values["value"] = quote(_to_string(value), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_me(self) -> MeResponse:
        """
        Gets the current user.

        Args:
        """

        __url = "/api/v1/users/me"

        return self.___invoke(MeResponse, "GET", __url, "application/json", None, None)

    def create_token(self, token: PersonalAccessToken, user_id: Optional[str] = None) -> str:
        """
        Creates a personal access token.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/users/tokens/create"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(str, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(token, _json_encoder_options)))

    def delete_token(self, token_id: UUID) -> Response:
        """
        Deletes a personal access token.

        Args:
            token_id: The identifier of the personal access token.
        """

        __url = "/api/v1/users/tokens/{tokenId}"
        __url = __url.replace("{tokenId}", quote(str(token_id), safe=""))

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def accept_license(self, catalog_id: str) -> Response:
        """
        Accepts the license of the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/users/accept-license"

        __query_values: dict[str, str] = {}

        __query_values["catalogId"] = quote(_to_string(catalog_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Response, "GET", __url, "application/octet-stream", None, None)

    def get_users(self) -> dict[str, NexusUser]:
        """
        Gets a list of users.

        Args:
        """

        __url = "/api/v1/users"

        return self.___invoke(dict[str, NexusUser], "GET", __url, "application/json", None, None)

    def create_user(self, user: NexusUser) -> str:
        """
        Creates a user.

        Args:
        """

        __url = "/api/v1/users"

        return self.___invoke(str, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(user, _json_encoder_options)))

    def delete_user(self, user_id: str) -> Response:
        """
        Deletes a user.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_claims(self, user_id: str) -> dict[str, NexusClaim]:
        """
        Gets all claims.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/claims"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___invoke(dict[str, NexusClaim], "GET", __url, "application/json", None, None)

    def create_claim(self, user_id: str, claim: NexusClaim) -> UUID:
        """
        Creates a claim.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/claims"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(claim, _json_encoder_options)))

    def delete_claim(self, claim_id: UUID) -> Response:
        """
        Deletes a claim.

        Args:
            claim_id: The identifier of the claim.
        """

        __url = "/api/v1/users/claims/{claimId}"
        __url = __url.replace("{claimId}", quote(str(claim_id), safe=""))

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_tokens(self, user_id: str) -> dict[str, PersonalAccessToken]:
        """
        Gets all personal access tokens.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/tokens"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___invoke(dict[str, PersonalAccessToken], "GET", __url, "application/json", None, None)


class WritersClient:
    """Provides methods to interact with writers."""

    ___invoke: HttpRequestHandler
    
    def __init__(self, invoke: HttpRequestHandler):
        self.___invoke = invoke

    def get_descriptions(self) -> list[ExtensionDescription]:
        """
        Gets the list of writer descriptions.

        Args:
        """

        __url = "/api/v1/writers/descriptions"

        return self.___invoke(list[ExtensionDescription], "GET", __url, "application/json", None, None)



class V1Async:
    """A client for version V1."""
    
    _artifacts: ArtifactsAsyncClient
    _catalogs: CatalogsAsyncClient
    _data: DataAsyncClient
    _jobs: JobsAsyncClient
    _packageReferences: PackageReferencesAsyncClient
    _sources: SourcesAsyncClient
    _system: SystemAsyncClient
    _users: UsersAsyncClient
    _writers: WritersAsyncClient


    def __init__(self, invoke: HttpRequestHandlerAsync):
        """
        Initializes a new instance of V1Async
        
            Args:
                client: The client to use.
        """

        self._artifacts = ArtifactsAsyncClient(invoke)
        self._catalogs = CatalogsAsyncClient(invoke)
        self._data = DataAsyncClient(invoke)
        self._jobs = JobsAsyncClient(invoke)
        self._packageReferences = PackageReferencesAsyncClient(invoke)
        self._sources = SourcesAsyncClient(invoke)
        self._system = SystemAsyncClient(invoke)
        self._users = UsersAsyncClient(invoke)
        self._writers = WritersAsyncClient(invoke)


    @property
    def artifacts(self) -> ArtifactsAsyncClient:
        """Gets the ArtifactsAsyncClient."""
        return self._artifacts

    @property
    def catalogs(self) -> CatalogsAsyncClient:
        """Gets the CatalogsAsyncClient."""
        return self._catalogs

    @property
    def data(self) -> DataAsyncClient:
        """Gets the DataAsyncClient."""
        return self._data

    @property
    def jobs(self) -> JobsAsyncClient:
        """Gets the JobsAsyncClient."""
        return self._jobs

    @property
    def package_references(self) -> PackageReferencesAsyncClient:
        """Gets the PackageReferencesAsyncClient."""
        return self._packageReferences

    @property
    def sources(self) -> SourcesAsyncClient:
        """Gets the SourcesAsyncClient."""
        return self._sources

    @property
    def system(self) -> SystemAsyncClient:
        """Gets the SystemAsyncClient."""
        return self._system

    @property
    def users(self) -> UsersAsyncClient:
        """Gets the UsersAsyncClient."""
        return self._users

    @property
    def writers(self) -> WritersAsyncClient:
        """Gets the WritersAsyncClient."""
        return self._writers



class ArtifactsAsyncClient:
    """Provides methods to interact with artifacts."""

    ___invoke: HttpRequestHandlerAsync
    
    def __init__(self, invoke: HttpRequestHandlerAsync):
        self.___invoke = invoke

    def download(self, artifact_id: str) -> Awaitable[Response]:
        """
        Gets the specified artifact.

        Args:
            artifact_id: The artifact identifier.
        """

        __url = "/api/v1/artifacts/{artifactId}"
        __url = __url.replace("{artifactId}", quote(str(artifact_id), safe=""))

        return self.___invoke(Response, "GET", __url, "application/octet-stream", None, None)


class CatalogsAsyncClient:
    """Provides methods to interact with catalogs."""

    ___invoke: HttpRequestHandlerAsync
    
    def __init__(self, invoke: HttpRequestHandlerAsync):
        self.___invoke = invoke

    def search_catalog_items(self, resource_paths: list[str]) -> Awaitable[dict[str, CatalogItem]]:
        """
        Searches for the given resource paths and returns the corresponding catalog items.

        Args:
        """

        __url = "/api/v1/catalogs/search-items"

        return self.___invoke(dict[str, CatalogItem], "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(resource_paths, _json_encoder_options)))

    def get(self, catalog_id: str) -> Awaitable[ResourceCatalog]:
        """
        Gets the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(ResourceCatalog, "GET", __url, "application/json", None, None)

    def get_child_catalog_infos(self, catalog_id: str) -> Awaitable[list[CatalogInfo]]:
        """
        Gets a list of child catalog info for the provided parent catalog identifier.

        Args:
            catalog_id: The parent catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/child-catalog-infos"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(list[CatalogInfo], "GET", __url, "application/json", None, None)

    def get_time_range(self, catalog_id: str) -> Awaitable[CatalogTimeRange]:
        """
        Gets the specified catalog's time range.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/timerange"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(CatalogTimeRange, "GET", __url, "application/json", None, None)

    def get_availability(self, catalog_id: str, begin: datetime, end: datetime, step: timedelta) -> Awaitable[CatalogAvailability]:
        """
        Gets the specified catalog's availability.

        Args:
            catalog_id: The catalog identifier.
            begin: Start date/time.
            end: End date/time.
            step: Step period.
        """

        __url = "/api/v1/catalogs/{catalogId}/availability"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        __query_values: dict[str, str] = {}

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query_values["step"] = quote(_to_string(step), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(CatalogAvailability, "GET", __url, "application/json", None, None)

    def get_license(self, catalog_id: str) -> Awaitable[Optional[str]]:
        """
        Gets the license of the catalog if available.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/license"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(str, "GET", __url, "application/json", None, None)

    def get_attachments(self, catalog_id: str) -> Awaitable[list[str]]:
        """
        Gets all attachments for the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(list[str], "GET", __url, "application/json", None, None)

    def upload_attachment(self, catalog_id: str, attachment_id: str, content: Union[bytes, Iterable[bytes], AsyncIterable[bytes]]) -> Awaitable[Response]:
        """
        Uploads the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___invoke(Response, "PUT", __url, "application/octet-stream", "application/octet-stream", content)

    def delete_attachment(self, catalog_id: str, attachment_id: str) -> Awaitable[Response]:
        """
        Deletes the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_attachment_stream(self, catalog_id: str, attachment_id: str) -> Awaitable[Response]:
        """
        Gets the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___invoke(Response, "GET", __url, "application/octet-stream", None, None)

    def get_metadata(self, catalog_id: str) -> Awaitable[CatalogMetadata]:
        """
        Gets the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/metadata"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(CatalogMetadata, "GET", __url, "application/json", None, None)

    def set_metadata(self, catalog_id: str, metadata: CatalogMetadata) -> Awaitable[Response]:
        """
        Puts the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/metadata"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___invoke(Response, "PUT", __url, "application/octet-stream", "application/json", json.dumps(JsonEncoder.encode(metadata, _json_encoder_options)))


class DataAsyncClient:
    """Provides methods to interact with data."""

    ___invoke: HttpRequestHandlerAsync
    
    def __init__(self, invoke: HttpRequestHandlerAsync):
        self.___invoke = invoke

    def get_stream(self, resource_path: str, begin: datetime, end: datetime) -> Awaitable[Response]:
        """
        Gets the requested data.

        Args:
            resource_path: The path to the resource data to stream.
            begin: Start date/time.
            end: End date/time.
        """

        __url = "/api/v1/data"

        __query_values: dict[str, str] = {}

        __query_values["resourcePath"] = quote(_to_string(resource_path), safe="")

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Response, "GET", __url, "application/octet-stream", None, None)


class JobsAsyncClient:
    """Provides methods to interact with jobs."""

    ___invoke: HttpRequestHandlerAsync
    
    def __init__(self, invoke: HttpRequestHandlerAsync):
        self.___invoke = invoke

    def get_jobs(self) -> Awaitable[list[Job]]:
        """
        Gets a list of jobs.

        Args:
        """

        __url = "/api/v1/jobs"

        return self.___invoke(list[Job], "GET", __url, "application/json", None, None)

    def cancel_job(self, job_id: UUID) -> Awaitable[Response]:
        """
        Cancels the specified job.

        Args:
            job_id: 
        """

        __url = "/api/v1/jobs/{jobId}"
        __url = __url.replace("{jobId}", quote(str(job_id), safe=""))

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_job_status(self, job_id: UUID) -> Awaitable[JobStatus]:
        """
        Gets the status of the specified job.

        Args:
            job_id: 
        """

        __url = "/api/v1/jobs/{jobId}/status"
        __url = __url.replace("{jobId}", quote(str(job_id), safe=""))

        return self.___invoke(JobStatus, "GET", __url, "application/json", None, None)

    def export(self, parameters: ExportParameters) -> Awaitable[Job]:
        """
        Creates a new export job.

        Args:
        """

        __url = "/api/v1/jobs/export"

        return self.___invoke(Job, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(parameters, _json_encoder_options)))

    def refresh_database(self) -> Awaitable[Job]:
        """
        Creates a new job which reloads all extensions and resets the resource catalog.

        Args:
        """

        __url = "/api/v1/jobs/refresh-database"

        return self.___invoke(Job, "POST", __url, "application/json", None, None)

    def clear_cache(self, catalog_id: str, begin: datetime, end: datetime) -> Awaitable[Job]:
        """
        Clears the aggregation data cache for the specified period of time.

        Args:
            catalog_id: The catalog identifier.
            begin: Start date/time.
            end: End date/time.
        """

        __url = "/api/v1/jobs/clear-cache"

        __query_values: dict[str, str] = {}

        __query_values["catalogId"] = quote(_to_string(catalog_id), safe="")

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Job, "POST", __url, "application/json", None, None)


class PackageReferencesAsyncClient:
    """Provides methods to interact with package references."""

    ___invoke: HttpRequestHandlerAsync
    
    def __init__(self, invoke: HttpRequestHandlerAsync):
        self.___invoke = invoke

    def get(self) -> Awaitable[dict[str, PackageReference]]:
        """
        Gets the list of package references.

        Args:
        """

        __url = "/api/v1/packagereferences"

        return self.___invoke(dict[str, PackageReference], "GET", __url, "application/json", None, None)

    def create(self, package_reference: PackageReference) -> Awaitable[UUID]:
        """
        Creates a package reference.

        Args:
        """

        __url = "/api/v1/packagereferences"

        return self.___invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(package_reference, _json_encoder_options)))

    def delete(self, id: UUID) -> Awaitable[None]:
        """
        Deletes a package reference.

        Args:
            id: The ID of the package reference.
        """

        __url = "/api/v1/packagereferences/{id}"
        __url = __url.replace("{id}", quote(str(id), safe=""))

        return self.___invoke(type(None), "DELETE", __url, None, None, None)

    def get_versions(self, id: UUID) -> Awaitable[list[str]]:
        """
        Gets package versions.

        Args:
            id: The ID of the package reference.
        """

        __url = "/api/v1/packagereferences/{id}/versions"
        __url = __url.replace("{id}", quote(str(id), safe=""))

        return self.___invoke(list[str], "GET", __url, "application/json", None, None)


class SourcesAsyncClient:
    """Provides methods to interact with sources."""

    ___invoke: HttpRequestHandlerAsync
    
    def __init__(self, invoke: HttpRequestHandlerAsync):
        self.___invoke = invoke

    def get_descriptions(self) -> Awaitable[list[ExtensionDescription]]:
        """
        Gets the list of source descriptions.

        Args:
        """

        __url = "/api/v1/sources/descriptions"

        return self.___invoke(list[ExtensionDescription], "GET", __url, "application/json", None, None)

    def get_pipelines(self, user_id: Optional[str] = None) -> Awaitable[dict[str, DataSourcePipeline]]:
        """
        Gets the list of data source pipelines.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/pipelines"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(dict[str, DataSourcePipeline], "GET", __url, "application/json", None, None)

    def create_pipeline(self, pipeline: DataSourcePipeline, user_id: Optional[str] = None) -> Awaitable[UUID]:
        """
        Creates a data source pipeline.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/pipelines"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(pipeline, _json_encoder_options)))

    def delete_pipeline(self, pipeline_id: UUID, user_id: Optional[str] = None) -> Awaitable[Response]:
        """
        Deletes a data source pipeline.

        Args:
            pipeline_id: The identifier of the pipeline.
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/pipelines/{pipelineId}"
        __url = __url.replace("{pipelineId}", quote(str(pipeline_id), safe=""))

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)


class SystemAsyncClient:
    """Provides methods to interact with system."""

    ___invoke: HttpRequestHandlerAsync
    
    def __init__(self, invoke: HttpRequestHandlerAsync):
        self.___invoke = invoke

    def get_default_file_type(self) -> Awaitable[str]:
        """
        Gets the default file type.

        Args:
        """

        __url = "/api/v1/system/file-type"

        return self.___invoke(str, "GET", __url, "application/json", None, None)

    def get_help_link(self) -> Awaitable[str]:
        """
        Gets the configured help link.

        Args:
        """

        __url = "/api/v1/system/help-link"

        return self.___invoke(str, "GET", __url, "application/json", None, None)

    def get_configuration(self) -> Awaitable[Optional[dict[str, object]]]:
        """
        Gets the system configuration.

        Args:
        """

        __url = "/api/v1/system/configuration"

        return self.___invoke(dict[str, object], "GET", __url, "application/json", None, None)

    def set_configuration(self, configuration: Optional[dict[str, object]]) -> Awaitable[None]:
        """
        Sets the system configuration.

        Args:
        """

        __url = "/api/v1/system/configuration"

        return self.___invoke(type(None), "PUT", __url, None, "application/json", json.dumps(JsonEncoder.encode(configuration, _json_encoder_options)))


class UsersAsyncClient:
    """Provides methods to interact with users."""

    ___invoke: HttpRequestHandlerAsync
    
    def __init__(self, invoke: HttpRequestHandlerAsync):
        self.___invoke = invoke

    def authenticate(self, scheme: str, return_url: str) -> Awaitable[Response]:
        """
        Authenticates the user.

        Args:
            scheme: The authentication scheme to challenge.
            return_url: The URL to return after successful authentication.
        """

        __url = "/api/v1/users/authenticate"

        __query_values: dict[str, str] = {}

        __query_values["scheme"] = quote(_to_string(scheme), safe="")

        __query_values["returnUrl"] = quote(_to_string(return_url), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Response, "POST", __url, "application/octet-stream", None, None)

    def sign_out(self, return_url: str) -> Awaitable[None]:
        """
        Logs out the user.

        Args:
            return_url: The URL to return after logout.
        """

        __url = "/api/v1/users/signout"

        __query_values: dict[str, str] = {}

        __query_values["returnUrl"] = quote(_to_string(return_url), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(type(None), "POST", __url, None, None, None)

    def delete_token_by_value(self, value: str) -> Awaitable[Response]:
        """
        Deletes a personal access token.

        Args:
            value: The personal access token to delete.
        """

        __url = "/api/v1/users/tokens/delete"

        __query_values: dict[str, str] = {}

        __query_values["value"] = quote(_to_string(value), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_me(self) -> Awaitable[MeResponse]:
        """
        Gets the current user.

        Args:
        """

        __url = "/api/v1/users/me"

        return self.___invoke(MeResponse, "GET", __url, "application/json", None, None)

    def create_token(self, token: PersonalAccessToken, user_id: Optional[str] = None) -> Awaitable[str]:
        """
        Creates a personal access token.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/users/tokens/create"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(str, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(token, _json_encoder_options)))

    def delete_token(self, token_id: UUID) -> Awaitable[Response]:
        """
        Deletes a personal access token.

        Args:
            token_id: The identifier of the personal access token.
        """

        __url = "/api/v1/users/tokens/{tokenId}"
        __url = __url.replace("{tokenId}", quote(str(token_id), safe=""))

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def accept_license(self, catalog_id: str) -> Awaitable[Response]:
        """
        Accepts the license of the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/users/accept-license"

        __query_values: dict[str, str] = {}

        __query_values["catalogId"] = quote(_to_string(catalog_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___invoke(Response, "GET", __url, "application/octet-stream", None, None)

    def get_users(self) -> Awaitable[dict[str, NexusUser]]:
        """
        Gets a list of users.

        Args:
        """

        __url = "/api/v1/users"

        return self.___invoke(dict[str, NexusUser], "GET", __url, "application/json", None, None)

    def create_user(self, user: NexusUser) -> Awaitable[str]:
        """
        Creates a user.

        Args:
        """

        __url = "/api/v1/users"

        return self.___invoke(str, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(user, _json_encoder_options)))

    def delete_user(self, user_id: str) -> Awaitable[Response]:
        """
        Deletes a user.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_claims(self, user_id: str) -> Awaitable[dict[str, NexusClaim]]:
        """
        Gets all claims.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/claims"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___invoke(dict[str, NexusClaim], "GET", __url, "application/json", None, None)

    def create_claim(self, user_id: str, claim: NexusClaim) -> Awaitable[UUID]:
        """
        Creates a claim.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/claims"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(claim, _json_encoder_options)))

    def delete_claim(self, claim_id: UUID) -> Awaitable[Response]:
        """
        Deletes a claim.

        Args:
            claim_id: The identifier of the claim.
        """

        __url = "/api/v1/users/claims/{claimId}"
        __url = __url.replace("{claimId}", quote(str(claim_id), safe=""))

        return self.___invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_tokens(self, user_id: str) -> Awaitable[dict[str, PersonalAccessToken]]:
        """
        Gets all personal access tokens.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/tokens"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___invoke(dict[str, PersonalAccessToken], "GET", __url, "application/json", None, None)


class WritersAsyncClient:
    """Provides methods to interact with writers."""

    ___invoke: HttpRequestHandlerAsync
    
    def __init__(self, invoke: HttpRequestHandlerAsync):
        self.___invoke = invoke

    def get_descriptions(self) -> Awaitable[list[ExtensionDescription]]:
        """
        Gets the list of writer descriptions.

        Args:
        """

        __url = "/api/v1/writers/descriptions"

        return self.___invoke(list[ExtensionDescription], "GET", __url, "application/json", None, None)



@dataclass(frozen=True)
class CatalogItem:
    """
    A catalog item consists of a catalog, a resource and a representation.

    Args:
        catalog: The catalog.
        resource: The resource.
        representation: The representation.
        parameters: The optional dictionary of representation parameters and its arguments.
    """

    catalog: ResourceCatalog
    """The catalog."""

    resource: Resource
    """The resource."""

    representation: Representation
    """The representation."""

    parameters: Optional[dict[str, str]]
    """The optional dictionary of representation parameters and its arguments."""


@dataclass(frozen=True)
class ResourceCatalog:
    """
    A catalog is a top level element and holds a list of resources.

    Args:
        id: Gets the identifier.
        properties: Gets the properties.
        resources: Gets the list of representations.
    """

    id: str
    """Gets the identifier."""

    properties: Optional[dict[str, object]]
    """Gets the properties."""

    resources: Optional[list[Resource]]
    """Gets the list of representations."""


@dataclass(frozen=True)
class Resource:
    """
    A resource is part of a resource catalog and holds a list of representations.

    Args:
        id: Gets the identifier.
        properties: Gets the properties.
        representations: Gets the list of representations.
    """

    id: str
    """Gets the identifier."""

    properties: Optional[dict[str, object]]
    """Gets the properties."""

    representations: Optional[list[Representation]]
    """Gets the list of representations."""


@dataclass(frozen=True)
class Representation:
    """
    A representation is part of a resource.

    Args:
        data_type: The data type.
        sample_period: The sample period.
        parameters: The optional list of parameters.
    """

    data_type: NexusDataType
    """The data type."""

    sample_period: timedelta
    """The sample period."""

    parameters: Optional[dict[str, object]]
    """The optional list of parameters."""


class NexusDataType(Enum):
    """Specifies the Nexus data type."""

    UINT8 = "UINT8"
    """UINT8"""

    UINT16 = "UINT16"
    """UINT16"""

    UINT32 = "UINT32"
    """UINT32"""

    UINT64 = "UINT64"
    """UINT64"""

    INT8 = "INT8"
    """INT8"""

    INT16 = "INT16"
    """INT16"""

    INT32 = "INT32"
    """INT32"""

    INT64 = "INT64"
    """INT64"""

    FLOAT32 = "FLOAT32"
    """FLOAT32"""

    FLOAT64 = "FLOAT64"
    """FLOAT64"""


@dataclass(frozen=True)
class CatalogInfo:
    """
    A structure for catalog information.

    Args:
        id: The identifier.
        title: A nullable title.
        contact: A nullable contact.
        readme: A nullable readme.
        license: A nullable license.
        is_readable: A boolean which indicates if the catalog is accessible.
        is_writable: A boolean which indicates if the catalog is editable.
        is_released: A boolean which indicates if the catalog is released.
        is_visible: A boolean which indicates if the catalog is visible.
        is_owner: A boolean which indicates if the catalog is owned by the current user.
        package_reference_ids: The package reference identifiers.
        pipeline_info: A structure for pipeline info.
    """

    id: str
    """The identifier."""

    title: Optional[str]
    """A nullable title."""

    contact: Optional[str]
    """A nullable contact."""

    readme: Optional[str]
    """A nullable readme."""

    license: Optional[str]
    """A nullable license."""

    is_readable: bool
    """A boolean which indicates if the catalog is accessible."""

    is_writable: bool
    """A boolean which indicates if the catalog is editable."""

    is_released: bool
    """A boolean which indicates if the catalog is released."""

    is_visible: bool
    """A boolean which indicates if the catalog is visible."""

    is_owner: bool
    """A boolean which indicates if the catalog is owned by the current user."""

    package_reference_ids: list[UUID]
    """The package reference identifiers."""

    pipeline_info: PipelineInfo
    """A structure for pipeline info."""


@dataclass(frozen=True)
class PipelineInfo:
    """
    A structure for pipeline information.

    Args:
        id: The pipeline identifier.
        types: An array of data source types.
        info_urls: An array of data source info URLs.
    """

    id: UUID
    """The pipeline identifier."""

    types: list[str]
    """An array of data source types."""

    info_urls: list[Optional[str]]
    """An array of data source info URLs."""


@dataclass(frozen=True)
class CatalogTimeRange:
    """
    A catalog time range.

    Args:
        begin: The date/time of the first data in the catalog.
        end: The date/time of the last data in the catalog.
    """

    begin: datetime
    """The date/time of the first data in the catalog."""

    end: datetime
    """The date/time of the last data in the catalog."""


@dataclass(frozen=True)
class CatalogAvailability:
    """
    The catalog availability.

    Args:
        data: The actual availability data.
    """

    data: list[float]
    """The actual availability data."""


@dataclass(frozen=True)
class CatalogMetadata:
    """
    A structure for catalog metadata.

    Args:
        contact: The contact.
        group_memberships: A list of groups the catalog is part of.
        overrides: Overrides for the catalog.
    """

    contact: Optional[str]
    """The contact."""

    group_memberships: Optional[list[str]]
    """A list of groups the catalog is part of."""

    overrides: Optional[ResourceCatalog]
    """Overrides for the catalog."""


@dataclass(frozen=True)
class Job:
    """
    Description of a job.

    Args:
        id: The global unique identifier.
        type: The job type.
        owner: The owner of the job.
        parameters: The job parameters.
    """

    id: UUID
    """The global unique identifier."""

    type: str
    """The job type."""

    owner: str
    """The owner of the job."""

    parameters: Optional[object]
    """The job parameters."""


@dataclass(frozen=True)
class JobStatus:
    """
    Describes the status of the job.

    Args:
        start: The start date/time.
        status: The status.
        progress: The progress from 0 to 1.
        exception_message: The nullable exception message.
        result: The nullable result.
    """

    start: datetime
    """The start date/time."""

    status: TaskStatus
    """The status."""

    progress: float
    """The progress from 0 to 1."""

    exception_message: Optional[str]
    """The nullable exception message."""

    result: Optional[object]
    """The nullable result."""


class TaskStatus(Enum):
    """"""

    CREATED = "CREATED"
    """Created"""

    WAITING_FOR_ACTIVATION = "WAITING_FOR_ACTIVATION"
    """WaitingForActivation"""

    WAITING_TO_RUN = "WAITING_TO_RUN"
    """WaitingToRun"""

    RUNNING = "RUNNING"
    """Running"""

    WAITING_FOR_CHILDREN_TO_COMPLETE = "WAITING_FOR_CHILDREN_TO_COMPLETE"
    """WaitingForChildrenToComplete"""

    RAN_TO_COMPLETION = "RAN_TO_COMPLETION"
    """RanToCompletion"""

    CANCELED = "CANCELED"
    """Canceled"""

    FAULTED = "FAULTED"
    """Faulted"""


@dataclass(frozen=True)
class ExportParameters:
    """
    A structure for export parameters.

    Args:
        begin: The start date/time.
        end: The end date/time.
        file_period: The file period.
        type: The writer type. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation.
        resource_paths: The resource paths to export.
        configuration: The configuration.
    """

    begin: datetime
    """The start date/time."""

    end: datetime
    """The end date/time."""

    file_period: timedelta
    """The file period."""

    type: Optional[str]
    """The writer type. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation."""

    resource_paths: list[str]
    """The resource paths to export."""

    configuration: Optional[dict[str, object]]
    """The configuration."""


@dataclass(frozen=True)
class PackageReference:
    """
    A package reference.

    Args:
        provider: The provider which loads the package.
        configuration: The configuration of the package reference.
    """

    provider: str
    """The provider which loads the package."""

    configuration: dict[str, str]
    """The configuration of the package reference."""


@dataclass(frozen=True)
class ExtensionDescription:
    """
    An extension description.

    Args:
        type: The extension type.
        version: The extension version.
        description: A nullable description.
        project_url: A nullable project website URL.
        repository_url: A nullable source repository URL.
        additional_information: Additional information about the extension.
    """

    type: str
    """The extension type."""

    version: str
    """The extension version."""

    description: Optional[str]
    """A nullable description."""

    project_url: Optional[str]
    """A nullable project website URL."""

    repository_url: Optional[str]
    """A nullable source repository URL."""

    additional_information: Optional[dict[str, object]]
    """Additional information about the extension."""


@dataclass(frozen=True)
class DataSourcePipeline:
    """
    A data source pipeline.

    Args:
        registrations: The list of pipeline elements (data source registrations).
        release_pattern: An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released.
        visibility_pattern: An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible.
    """

    registrations: list[DataSourceRegistration]
    """The list of pipeline elements (data source registrations)."""

    release_pattern: Optional[str]
    """An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released."""

    visibility_pattern: Optional[str]
    """An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible."""


@dataclass(frozen=True)
class DataSourceRegistration:
    """
    A data source registration.

    Args:
        type: The type of the data source.
        resource_locator: An optional URL which points to the data.
        configuration: Configuration parameters for the instantiated source.
        info_url: An optional info URL.
    """

    type: str
    """The type of the data source."""

    resource_locator: Optional[str]
    """An optional URL which points to the data."""

    configuration: Optional[dict[str, object]]
    """Configuration parameters for the instantiated source."""

    info_url: Optional[str]
    """An optional info URL."""


@dataclass(frozen=True)
class MeResponse:
    """
    A me response.

    Args:
        user_id: The user id.
        user: The user.
        is_admin: A boolean which indicates if the user is an administrator.
        personal_access_tokens: A list of personal access tokens.
    """

    user_id: str
    """The user id."""

    user: NexusUser
    """The user."""

    is_admin: bool
    """A boolean which indicates if the user is an administrator."""

    personal_access_tokens: dict[str, PersonalAccessToken]
    """A list of personal access tokens."""


@dataclass(frozen=True)
class NexusUser:
    """
    Represents a user.

    Args:
        name: The user name.
    """

    name: str
    """The user name."""


@dataclass(frozen=True)
class PersonalAccessToken:
    """
    A personal access token.

    Args:
        description: The token description.
        expires: The date/time when the token expires.
        claims: The claims that will be part of the token.
    """

    description: str
    """The token description."""

    expires: datetime
    """The date/time when the token expires."""

    claims: list[TokenClaim]
    """The claims that will be part of the token."""


@dataclass(frozen=True)
class TokenClaim:
    """
    A revoke token request.

    Args:
        type: The claim type.
        value: The claim value.
    """

    type: str
    """The claim type."""

    value: str
    """The claim value."""


@dataclass(frozen=True)
class NexusClaim:
    """
    Represents a claim.

    Args:
        type: The claim type.
        value: The claim value.
    """

    type: str
    """The claim type."""

    value: str
    """The claim value."""



