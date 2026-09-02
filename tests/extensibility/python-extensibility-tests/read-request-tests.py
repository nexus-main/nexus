import asyncio
from datetime import timedelta

from nexus_extensibility import (CatalogItem, NexusDataType, ReadRequest,
                                 Representation, Resource, ResourceCatalog)


class ReadRequestTests:
    def completion_is_serialized_and_retryable_test(self):
        asyncio.run(self._completion_is_serialized_and_retryable())

    async def _completion_is_serialized_and_retryable(self):
        calls = 0

        async def complete():
            nonlocal calls
            calls += 1
            await asyncio.sleep(0)

            if calls == 1:
                raise RuntimeError("failed")

        item = CatalogItem(
            ResourceCatalog("/catalog"),
            Resource("resource"),
            Representation(NexusDataType.FLOAT64, timedelta(seconds=1)),
            None)
        request = ReadRequest("resource", item, memoryview(b""), memoryview(b""), complete)

        results = await asyncio.gather(request.complete(), request.complete(), return_exceptions=True)

        assert isinstance(results[0], RuntimeError)
        assert results[1] is None
        assert calls == 2
        assert request.is_completed
