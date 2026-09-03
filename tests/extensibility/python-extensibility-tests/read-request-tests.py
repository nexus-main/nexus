import asyncio
from datetime import timedelta

from nexus_extensibility import (CatalogItem, NexusDataType, ReadRequest,
                                 Representation, Resource, ResourceCatalog)


class ReadRequestTests:
    def concurrent_completion_runs_once_test(self):
        asyncio.run(self._concurrent_completion_runs_once())

    async def _concurrent_completion_runs_once(self):
        calls = 0

        async def complete():
            nonlocal calls
            calls += 1

        request = self._create_request(complete)

        await asyncio.gather(*(request.complete() for _ in range(10)))

        assert calls == 1

    def completion_waits_for_callback_test(self):
        asyncio.run(self._completion_waits_for_callback())

    async def _completion_waits_for_callback(self):
        event = asyncio.Event()

        async def complete():
            await event.wait()

        request = self._create_request(complete)
        task = asyncio.create_task(request.complete())

        await asyncio.sleep(0)
        assert not task.done()

        event.set()
        await task

    def failed_completion_is_not_retried_test(self):
        asyncio.run(self._failed_completion_is_not_retried())

    async def _failed_completion_is_not_retried(self):
        calls = 0

        async def complete():
            nonlocal calls
            calls += 1
            await asyncio.sleep(0)

            raise RuntimeError("failed")

        request = self._create_request(complete)

        results = await asyncio.gather(request.complete(), request.complete(), return_exceptions=True)

        assert isinstance(results[0], RuntimeError)
        assert isinstance(results[1], RuntimeError)
        assert calls == 1

    @staticmethod
    def _create_request(on_completed):
        item = CatalogItem(
            ResourceCatalog("/catalog"),
            Resource("resource"),
            Representation(NexusDataType.FLOAT64, timedelta(seconds=1)),
            None)
        return ReadRequest("resource", item, memoryview(b""), memoryview(b""), on_completed)
