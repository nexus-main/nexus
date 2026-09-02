import asyncio
from datetime import timedelta
from typing import Any, cast

from nexus_extensibility import (CatalogItem, NexusDataType, ReadRequest,
                                 Representation, Resource, ResourceCatalog)


class ReadRequestTests:
    def completion_is_serialized_and_not_retried_test(self):
        asyncio.run(self._completion_is_serialized_and_not_retried())

    async def _completion_is_serialized_and_not_retried(self):
        calls = 0

        async def complete():
            nonlocal calls
            calls += 1
            await asyncio.sleep(0)

            raise RuntimeError("failed")

        item = CatalogItem(
            ResourceCatalog("/catalog"),
            Resource("resource"),
            Representation(NexusDataType.FLOAT64, timedelta(seconds=1)),
            None)
        request = ReadRequest("resource", item, memoryview(b""), memoryview(b""))
        cast(Any, request)._configure_completion(complete)

        results = await asyncio.gather(request.complete(), request.complete(), return_exceptions=True)

        assert isinstance(results[0], RuntimeError)
        assert isinstance(results[1], RuntimeError)
        assert calls == 1
        assert not request.is_completed
