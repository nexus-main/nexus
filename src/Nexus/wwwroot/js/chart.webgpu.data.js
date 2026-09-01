(function () {
    const ns = window.__nexusChartWebGpu;
    const { instances, dotNetHelpers, getInstance, valueOf, overviewBucketSize, reducedPointsPerBucket, syntheticStreamChunkLength, rawChunkLength, rangeWorkgroupSize, maxRangeWorkgroups } = ns;

    function getSyntheticWorker(instance) {
        if (instance.syntheticWorker)
            return instance.syntheticWorker;

        const worker = new Worker('js/chart.synthetic.worker.js');
        worker.onmessage = event => instance.workerCallbacks.get(event.data.requestId)?.onmessage(event);
        worker.onerror = event => {
            const callbacks = [...instance.workerCallbacks.values()];
            instance.workerCallbacks.clear();
            instance.syntheticWorker = null;
            worker.terminate();
            for (const callback of callbacks)
                callback.onerror(event);
        };
        instance.syntheticWorker = worker;
        return worker;
    }

    function cancelWorkerRequest(instance, requestId) {
        instance.workerCallbacks.delete(requestId);
        instance.syntheticWorker?.postMessage({ type: 'cancel', requestId });
    }

    function getSeriesKey(id, version, length) {
        return `${id}:${version}:${length}`;
    }

    function destroySeriesBuffer(instance, cached) {
        ns.destroyTrackedBuffer(instance, cached.buffer);

        if (cached.pointBuffer !== cached.buffer)
            ns.destroyTrackedBuffer(instance, cached.pointBuffer);

        for (const decimation of cached.decimations?.values() ?? []) {
            ns.destroyTrackedBuffer(instance, decimation.outputBuffer);
            ns.destroyTrackedBuffer(instance, decimation.paramsBuffer);
        }
    }

    function destroyRawChunk(instance, key, chunk) {
        destroySeriesBuffer(instance, chunk);
        instance.rawChunks.delete(key);
    }

    function evictRawChunks(instance, requiredBytes, protectedKeys = new Set(), budget = instance.cacheBudget) {
        const candidates = [...instance.rawChunks.entries()]
            .filter(([key]) => !protectedKeys.has(key))
            .sort((a, b) => a[1].lastUsed - b[1].lastUsed);

        while (instance.ownedGpuBytes + instance.rawReservedBytes + requiredBytes > budget && candidates.length) {
            const [key, chunk] = candidates.shift();
            destroyRawChunk(instance, key, chunk);
        }

        if (requiredBytes > 0 && instance.ownedGpuBytes + instance.rawReservedBytes + requiredBytes > budget) {
            const error = new Error(`Chart GPU memory budget (${budget} bytes) cannot fit a ${requiredBytes}-byte allocation`);
            error.webGpuCacheCapacity = true;
            throw error;
        }
    }

    function removeRawSeries(instance, id) {
        for (const [key, request] of instance.rawRequests) {
            if (request.id === id) {
                cancelWorkerRequest(instance, request.requestId);
                instance.rawReservedBytes -= request.byteLength;
                request.reject(ns.cancellationError(`Raw chunk request superseded for series ${id}`));
                instance.rawRequests.delete(key);
            }
        }

        for (const [key, chunk] of instance.rawChunks) {
            if (chunk.id === id)
                destroyRawChunk(instance, key, chunk);
        }
    }

    function cancelGeneration(instance, id, reason) {
        const job = instance.generationJobs.get(id);
        if (!job)
            return;

        job.cancelled = true;
        cancelWorkerRequest(instance, job.requestId);
        job.reject(ns.cancellationError(reason));
    }

    function synchronizeSeries(instance, activeIds) {
        const active = new Set(activeIds);

        for (const [key, cached] of instance.seriesBuffers) {
            if (active.has(cached.id))
                continue;

            destroySeriesBuffer(instance, cached);
            instance.seriesBuffers.delete(key);
            removeRawSeries(instance, cached.id);
            cancelGeneration(instance, cached.id, `Series ${cached.id} was removed`);
            instance.uploadGenerations.delete(cached.id);
        }

        for (const id of instance.generationJobs.keys()) {
            if (!active.has(id))
                cancelGeneration(instance, id, `Series ${id} was removed`);
        }

        for (const [token, upload] of instance.chunkedUploadSessions) {
            if (active.has(upload.id))
                continue;

            destroyChunkedUpload(instance, upload);
            instance.chunkedUploadSessions.delete(token);
        }
    }

    function destroyChunkedUpload(instance, upload) {
        ns.destroyTrackedBuffer(instance, upload.transientBuffer);
        ns.destroyTrackedBuffer(instance, upload.overviewBuffer);
        ns.destroyTrackedBuffer(instance, upload.paramsBuffer);
    }

    async function processOverviewChunkAsync(instance, transientBuffer, paramsBuffer, bindGroup, offset, values, count) {
        instance.device.queue.writeBuffer(transientBuffer, 0, values);
        const range = await calculateSeriesRangeAsync(instance, transientBuffer, count);
        instance.device.queue.writeBuffer(paramsBuffer, 0, new Uint32Array([
            offset, count, Math.floor(offset / overviewBucketSize), 0,
        ]));
        const encoder = instance.device.createCommandEncoder();
        const pass = encoder.beginComputePass();
        pass.setPipeline(instance.overviewPipeline);
        pass.setBindGroup(0, bindGroup);
        pass.dispatchWorkgroups(Math.ceil(count / overviewBucketSize));
        pass.end();
        instance.device.queue.submit([encoder.finish()]);
        await instance.device.queue.onSubmittedWorkDone();
        return range;
    }

    async function generateSyntheticSeriesAsync(chartId, id, version, length, kind) {
        const instance = await getInstance(chartId);

        if (!instance)
            throw new Error(`WebGPU instance unavailable for chart ${chartId}`);

        if (!Number.isSafeInteger(length) || length < 2 || length > 1000000000)
            throw new Error(`Synthetic series length must be an integer between 2 and 1,000,000,000 (received ${length})`);

        const generation = (instance.uploadGenerations.get(id) ?? 0) + 1;
        instance.uploadGenerations.set(id, generation);
        cancelGeneration(instance, id, `Synthetic generation superseded for series ${id}`);
        removeRawSeries(instance, id);
        const overviewBucketCount = Math.ceil(length / overviewBucketSize);
        const overviewLength = overviewBucketCount * reducedPointsPerBucket;
        const overviewBytes = overviewLength * 2 * Float32Array.BYTES_PER_ELEMENT;
        const deviceLimit = Math.min(instance.device.limits.maxBufferSize, instance.device.limits.maxStorageBufferBindingSize);

        if (overviewBytes > deviceLimit)
            throw new Error(`Persistent overview requires ${overviewBytes} bytes, exceeding the GPU storage buffer limit of ${deviceLimit} bytes`);

        const transientBytes = Math.min(syntheticStreamChunkLength, length) * Float32Array.BYTES_PER_ELEMENT;
        if (transientBytes > deviceLimit)
            throw new Error(`Synthetic stream chunk requires ${transientBytes} bytes, exceeding the GPU storage buffer limit of ${deviceLimit} bytes`);

        let transientBuffer = null;
        let overviewBuffer = null;
        let paramsBuffer = null;
        try {
            transientBuffer = ns.createTrackedBuffer(instance, {
                size: transientBytes,
                usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
            });
            overviewBuffer = ns.createTrackedBuffer(instance, { size: overviewBytes, usage: GPUBufferUsage.STORAGE });
            paramsBuffer = ns.createTrackedBuffer(instance, { size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
        } catch (error) {
            ns.destroyTrackedBuffer(instance, transientBuffer);
            ns.destroyTrackedBuffer(instance, overviewBuffer);
            ns.destroyTrackedBuffer(instance, paramsBuffer);
            throw error;
        }
        const bindGroup = instance.device.createBindGroup({
            layout: instance.overviewPipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: { buffer: transientBuffer } },
                { binding: 1, resource: { buffer: overviewBuffer } },
                { binding: 2, resource: { buffer: paramsBuffer } },
            ],
        });
        const worker = getSyntheticWorker(instance);
        const requestId = ++instance.workerRequestId;
        let rangeMinimum = 0;
        let rangeMaximum = 0;
        let rangeHasValue = false;

        let rejectGeneration;
        const job = {
            requestId,
            cancelled: false,
            handlerPromise: null,
            reject: error => rejectGeneration?.(error),
        };
        instance.generationJobs.set(id, job);

        function ensureGenerationIsActive() {
            if (job.cancelled || instances.get(chartId) !== instance || instance.uploadGenerations.get(id) !== generation)
                throw ns.cancellationError(`Synthetic generation superseded for series ${id}`);
        }

        try {
            await new Promise((resolve, reject) => {
                rejectGeneration = reject;
                instance.workerCallbacks.set(requestId, {
                    onerror: event => reject(new Error(event.message)),
                    onmessage: event => {
                        if (event.data.requestId !== requestId)
                            return;

                        if (event.data.complete) {
                            if (job.cancelled)
                                reject(ns.cancellationError(`Synthetic generation superseded for series ${id}`));
                            else
                                resolve();
                            return;
                        }

                        const handlerPromise = (async () => {
                            ensureGenerationIsActive();
                            const values = event.data.values;
                            const chunkRange = await processOverviewChunkAsync(
                                instance, transientBuffer, paramsBuffer, bindGroup,
                                event.data.offset, values, values.length);
                            ensureGenerationIsActive();
                            if (chunkRange.hasValue) {
                                rangeMinimum = rangeHasValue ? Math.min(rangeMinimum, chunkRange.minimum) : chunkRange.minimum;
                                rangeMaximum = rangeHasValue ? Math.max(rangeMaximum, chunkRange.maximum) : chunkRange.maximum;
                                rangeHasValue = true;
                            }
                            worker.postMessage({ type: 'ack', requestId });
                        })();
                        job.handlerPromise = handlerPromise;
                        handlerPromise.catch(error => {
                            reject(error);
                        }).finally(() => {
                            if (job.handlerPromise === handlerPromise)
                                job.handlerPromise = null;
                        });
                    },
                });
                worker.postMessage({ type: 'stream', requestId, length, kind, chunkLength: syntheticStreamChunkLength });
            });

            ensureGenerationIsActive();

            for (const [existingKey, existing] of instance.seriesBuffers) {
                if (existing.id === id) {
                    destroySeriesBuffer(instance, existing);
                    instance.seriesBuffers.delete(existingKey);
                }
            }

            const cached = {
                id, version, kind, length, buffer: overviewBuffer, pointBuffer: overviewBuffer,
                overviewLength, overviewBucketCount, byteLength: overviewBytes, dataMode: 1, synthetic: true, decimations: new Map(),
            };
            instance.seriesBuffers.set(getSeriesKey(id, version, length), cached);
            overviewBuffer = null;
            return { hasValue: rangeHasValue, minimum: rangeMinimum, maximum: rangeMaximum };
        } finally {
            if (job.handlerPromise) {
                try {
                    await job.handlerPromise;
                } catch {
                    // The outer generation promise reports the handler failure.
                }
            }

            if (instance.generationJobs.get(id) === job)
                instance.generationJobs.delete(id);
            ns.destroyTrackedBuffer(instance, transientBuffer);
            ns.destroyTrackedBuffer(instance, overviewBuffer);
            ns.destroyTrackedBuffer(instance, paramsBuffer);
            cancelWorkerRequest(instance, requestId);
        }
    }

    async function beginChunkedSeriesAsync(chartId, id, version, length) {
        const instance = await getInstance(chartId);
        if (!instance)
            throw new Error(`WebGPU instance unavailable for chart ${chartId}`);
        if (!Number.isSafeInteger(length) || length < 2)
            throw new Error(`Chunked series length must be a safe integer of at least 2 (received ${length})`);

        const overviewBucketCount = Math.ceil(length / overviewBucketSize);
        const overviewLength = overviewBucketCount * reducedPointsPerBucket;
        const overviewBytes = overviewLength * 2 * Float32Array.BYTES_PER_ELEMENT;
        const transientBytes = Math.min(syntheticStreamChunkLength, length) * Float32Array.BYTES_PER_ELEMENT;
        const deviceLimit = Math.min(instance.device.limits.maxBufferSize, instance.device.limits.maxStorageBufferBindingSize);
        if (overviewBytes > deviceLimit)
            throw new Error(`Persistent overview requires ${overviewBytes} bytes, exceeding the GPU storage buffer limit of ${deviceLimit} bytes`);
        if (transientBytes > deviceLimit)
            throw new Error(`Series stream chunk requires ${transientBytes} bytes, exceeding the GPU storage buffer limit of ${deviceLimit} bytes`);

        const generation = (instance.uploadGenerations.get(id) ?? 0) + 1;
        instance.uploadGenerations.set(id, generation);
        removeRawSeries(instance, id);
        for (const [token, upload] of instance.chunkedUploadSessions) {
            if (upload.id === id) {
                destroyChunkedUpload(instance, upload);
                instance.chunkedUploadSessions.delete(token);
            }
        }
        const token = ++instance.uploadToken;
        let transientBuffer = null;
        let overviewBuffer = null;
        let paramsBuffer = null;
        try {
            transientBuffer = ns.createTrackedBuffer(instance, {
                size: transientBytes,
                usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
            });
            overviewBuffer = ns.createTrackedBuffer(instance, {
                size: overviewBytes,
                usage: GPUBufferUsage.STORAGE,
            });
            paramsBuffer = ns.createTrackedBuffer(instance, {
                size: 16,
                usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
            });
            const bindGroup = instance.device.createBindGroup({
                layout: instance.overviewPipeline.getBindGroupLayout(0),
                entries: [
                    { binding: 0, resource: { buffer: transientBuffer } },
                    { binding: 1, resource: { buffer: overviewBuffer } },
                    { binding: 2, resource: { buffer: paramsBuffer } },
                ],
            });
            instance.chunkedUploadSessions.set(token, {
                id, version, length, generation, overviewLength, overviewBucketCount,
                transientBuffer, overviewBuffer, paramsBuffer, bindGroup,
                writtenLength: 0, rangeHasValue: false, rangeMinimum: 0, rangeMaximum: 0,
            });
            return token;
        } catch (error) {
            ns.destroyTrackedBuffer(instance, transientBuffer);
            ns.destroyTrackedBuffer(instance, overviewBuffer);
            ns.destroyTrackedBuffer(instance, paramsBuffer);
            throw error;
        }
    }

    async function appendChunkedSeriesAsync(chartId, token, offset, streamReference) {
        const instance = await getInstance(chartId);
        const upload = instance?.chunkedUploadSessions.get(token);
        if (!upload)
            throw ns.cancellationError(`Chunked series upload ${token} is no longer active`);
        const bytes = new Uint8Array(await streamReference.arrayBuffer());
        if (bytes.byteLength % Float32Array.BYTES_PER_ELEMENT !== 0)
            throw new Error(`Chunked series upload ${token} contains invalid data`);
        const count = bytes.byteLength / Float32Array.BYTES_PER_ELEMENT;
        if (offset !== upload.writtenLength || offset + count > upload.length)
            throw new Error(`Chunked series upload ${token} expected sample offset ${upload.writtenLength}, received ${offset}`);

        const range = await processOverviewChunkAsync(
            instance, upload.transientBuffer, upload.paramsBuffer,
            upload.bindGroup, offset, bytes, count);
        if (instances.get(chartId) !== instance || instance.chunkedUploadSessions.get(token) !== upload)
            throw ns.cancellationError(`Chunked series upload ${token} was superseded`);
        if (range.hasValue) {
            upload.rangeMinimum = upload.rangeHasValue ? Math.min(upload.rangeMinimum, range.minimum) : range.minimum;
            upload.rangeMaximum = upload.rangeHasValue ? Math.max(upload.rangeMaximum, range.maximum) : range.maximum;
            upload.rangeHasValue = true;
        }

        upload.writtenLength += count;
    }

    async function completeChunkedSeriesAsync(chartId, token) {
        const instance = await getInstance(chartId);
        const upload = instance?.chunkedUploadSessions.get(token);
        if (!upload)
            throw ns.cancellationError(`Chunked series upload ${token} is no longer active`);
        if (upload.writtenLength !== upload.length)
            throw new Error(`Chunked series upload ${token} is incomplete`);

        for (const [existingKey, existing] of instance.seriesBuffers) {
            if (existing.id === upload.id) {
                destroySeriesBuffer(instance, existing);
                instance.seriesBuffers.delete(existingKey);
            }
        }
        instance.seriesBuffers.set(getSeriesKey(upload.id, upload.version, upload.length), {
            id: upload.id, version: upload.version, length: upload.length,
            buffer: upload.overviewBuffer, pointBuffer: upload.overviewBuffer,
            overviewLength: upload.overviewLength, overviewBucketCount: upload.overviewBucketCount,
            dataMode: 1, chunked: true, decimations: new Map(),
        });
        ns.destroyTrackedBuffer(instance, upload.transientBuffer);
        ns.destroyTrackedBuffer(instance, upload.paramsBuffer);
        instance.chunkedUploadSessions.delete(token);
        return { hasValue: upload.rangeHasValue, minimum: upload.rangeMinimum, maximum: upload.rangeMaximum };
    }

    function abortChunkedSeries(chartId, token) {
        const instance = instances.get(chartId);
        const upload = instance?.chunkedUploadSessions.get(token);
        if (!upload)
            return;
        destroyChunkedUpload(instance, upload);
        instance.chunkedUploadSessions.delete(token);
    }

    function getSeriesBuffer(instance, series) {
        const id = valueOf(series, 'Id');
        const version = valueOf(series, 'DataVersion') ?? 0;
        const length = valueOf(series, 'Length') ?? 0;

        if (length < 2)
            return null;

        return instance.seriesBuffers.get(getSeriesKey(id, version, length)) ?? null;
    }

    function getPreviewRenderKey(instance, payload, width, height) {
        const zoom = valueOf(payload, 'Zoom') ?? {};
        const seriesList = valueOf(payload, 'Series') ?? [];
        const seriesKey = seriesList.map(series => {
            const id = valueOf(series, 'Id');
            const version = valueOf(series, 'DataVersion') ?? 0;
            const length = valueOf(series, 'Length') ?? 0;
            const color = valueOf(series, 'Color') ?? {};

            return [
                id,
                version,
                length,
                valueOf(series, 'SampleStep'),
                instance.seriesBuffers.has(getSeriesKey(id, version, length)),
                valueOf(series, 'OverviewAxisMin'),
                valueOf(series, 'OverviewAxisMax'),
                valueOf(color, 'Red'),
                valueOf(color, 'Green'),
                valueOf(color, 'Blue'),
                valueOf(color, 'Alpha'),
            ];
        });

        return JSON.stringify([
            width,
            height,
            valueOf(zoom, 'Left') ?? 0,
            valueOf(zoom, 'Right') ?? 1,
            valueOf(payload, 'LineWidth') ?? 0.7,
            valueOf(payload, 'FillOpacity') ?? 0.10,
            seriesKey,
        ]);
    }

    async function calculateSeriesRangeAsync(instance, source, length) {
        const workgroupCount = Math.min(maxRangeWorkgroups, Math.max(1, Math.ceil(length / rangeWorkgroupSize)));
        const resultSize = workgroupCount * 16;
        let resultBuffer = null;
        let readbackBuffer = null;
        let paramsBuffer = null;

        try {
            resultBuffer = ns.createTrackedBuffer(instance, { size: resultSize, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
            readbackBuffer = ns.createTrackedBuffer(instance, { size: resultSize, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
            paramsBuffer = ns.createTrackedBuffer(instance, { size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
            instance.device.queue.writeBuffer(paramsBuffer, 0, new Uint32Array([length, workgroupCount, 0, 0]));
            const bindGroup = instance.device.createBindGroup({
                layout: instance.rangePipeline.getBindGroupLayout(0),
                entries: [
                    { binding: 0, resource: { buffer: source } },
                    { binding: 1, resource: { buffer: resultBuffer } },
                    { binding: 2, resource: { buffer: paramsBuffer } },
                ],
            });
            const encoder = instance.device.createCommandEncoder();
            const pass = encoder.beginComputePass();
            pass.setPipeline(instance.rangePipeline);
            pass.setBindGroup(0, bindGroup);
            pass.dispatchWorkgroups(workgroupCount);
            pass.end();
            encoder.copyBufferToBuffer(resultBuffer, 0, readbackBuffer, 0, resultSize);
            instance.device.queue.submit([encoder.finish()]);
            await readbackBuffer.mapAsync(GPUMapMode.READ);

            const view = new DataView(readbackBuffer.getMappedRange());
            let minimum = 0;
            let maximum = 0;
            let hasValue = false;

            for (let i = 0; i < workgroupCount; i++) {
                const offset = i * 16;

                if (view.getUint32(offset + 8, true) === 0)
                    continue;

                const localMinimum = view.getFloat32(offset, true);
                const localMaximum = view.getFloat32(offset + 4, true);

                if (!Number.isFinite(localMinimum) || !Number.isFinite(localMaximum))
                    throw new Error(`WebGPU range reduction returned non-finite values for workgroup ${i}`);

                minimum = hasValue ? Math.min(minimum, localMinimum) : localMinimum;
                maximum = hasValue ? Math.max(maximum, localMaximum) : localMaximum;
                hasValue = true;
            }

            return { hasValue, minimum, maximum };
        } finally {
            if (readbackBuffer?.mapState === 'mapped')
                readbackBuffer.unmap();

            ns.destroyTrackedBuffer(instance, resultBuffer);
            ns.destroyTrackedBuffer(instance, readbackBuffer);
            ns.destroyTrackedBuffer(instance, paramsBuffer);
        }
    }
    function rawChunkKey(source, chunkIndex) {
        return `${source.id}:${source.version}:${chunkIndex}`;
    }

    function requestRawChunk(instance, source, chunkIndex, protectedKeys) {
        const key = rawChunkKey(source, chunkIndex);
        const cached = instance.rawChunks.get(key);

        if (cached) {
            cached.lastUsed = performance.now();
            return Promise.resolve(cached);
        }

        const pending = instance.rawRequests.get(key);
        if (pending)
            return pending.promise;

        const offset = chunkIndex * rawChunkLength;
        if (offset >= source.length)
            return Promise.resolve(null);

        const count = Math.min(rawChunkLength + (offset + rawChunkLength < source.length ? 1 : 0), source.length - offset);
        const byteLength = count * Float32Array.BYTES_PER_ELEMENT;
        evictRawChunks(instance, byteLength, protectedKeys);
        instance.rawReservedBytes += byteLength;

        const requestId = ++instance.workerRequestId;
        let resolveRequest;
        let rejectRequest;
        const promise = new Promise((resolve, reject) => {
            resolveRequest = resolve;
            rejectRequest = reject;
        });
        const request = { id: source.id, requestId, promise, reject: rejectRequest, byteLength };
        let reservationActive = true;
        instance.rawRequests.set(key, request);
        const callbacks = {
            byteLength,
            onerror: event => {
                instance.rawRequests.delete(key);
                instance.workerCallbacks.delete(requestId);
                if (reservationActive) {
                    instance.rawReservedBytes -= byteLength;
                    reservationActive = false;
                }
                rejectRequest(new Error(`Raw chunk ${chunkIndex} generation failed: ${event.message}`));
            },
            onmessage: event => {
                if (event.data.requestId !== requestId)
                    return;

                try {
                    if (instances.get(source.chartId) !== instance || instance.uploadGenerations.get(source.id) !== source.generation)
                        throw ns.cancellationError(`Raw chunk request superseded for series ${source.id}`);

                    const values = event.data.values;
                    instance.rawReservedBytes -= byteLength;
                    reservationActive = false;
                    const buffer = ns.createTrackedBuffer(instance, {
                        size: values.byteLength,
                        usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
                    });
                    instance.device.queue.writeBuffer(buffer, 0, values);
                    const chunk = {
                        id: source.id, buffer, pointBuffer: buffer, dataMode: 0, decimations: new Map(),
                        offset, length: values.length, byteLength: values.byteLength, lastUsed: performance.now(),
                    };
                    instance.rawChunks.set(key, chunk);
                    instance.rawRequests.delete(key);
                    instance.workerCallbacks.delete(requestId);
                    evictRawChunks(instance, 0);
                    resolveRequest(chunk);
                    rerenderLastPayloads(instance);
                } catch (error) {
                    instance.rawRequests.delete(key);
                    instance.workerCallbacks.delete(requestId);
                    if (reservationActive) {
                        instance.rawReservedBytes -= byteLength;
                        reservationActive = false;
                    }
                    rejectRequest(error);
                }
            },
        };
        instance.workerCallbacks.set(requestId, callbacks);
        if (source.synthetic) {
            getSyntheticWorker(instance).postMessage({ type: 'raw', requestId, offset, count, kind: source.kind });
        } else {
            const helper = dotNetHelpers.get(source.chartId);
            if (!helper)
                callbacks.onerror({ message: `Chart ${source.chartId} is no longer active` });
            else
                helper.invokeMethodAsync('ProvideSeriesChunk', source.id, offset, count, requestId)
                    .catch(error => callbacks.onerror({ message: error?.message ?? error }));
        }
        return promise;
    }

    async function provideSeriesChunkAsync(chartId, requestId, streamReference) {
        const instance = instances.get(chartId);
        const callbacks = instance?.workerCallbacks.get(requestId);
        if (!callbacks)
            return;

        const bytes = await streamReference.arrayBuffer();
        if (bytes.byteLength % Float32Array.BYTES_PER_ELEMENT !== 0)
            throw new Error(`Raw chunk response ${requestId} has an invalid byte length`);
        if (bytes.byteLength !== callbacks.byteLength)
            throw new Error(`Raw chunk response ${requestId} has an unexpected byte length`);
        callbacks.onmessage({ data: { requestId, values: new Float32Array(bytes) } });
    }

    function rerenderLastPayloads(instance) {
        for (const [target, payload] of instance.lastPayloads) {
            instance.previewRenderKeys.delete(target);
            ns.scheduleRender(instance.chartId, payload).catch(error => {
                    if (!ns.isCancellationError(error))
                        console.error('[chart-webgpu] raw rerender failed', error);
                });
        }
    }

    function getRawRenderItems(instance, source, series, payload, plot, encoder, target, protectedKeys) {
        const timeWindow = ns.getTimeWindow(payload, series, source.length);

        if (!timeWindow)
            return null;

        const { first: left, last: right, indexLeft, indexRange } = timeWindow;
        const span = right - left;

        if (!Number.isFinite(span) || span <= 0 || span > rawChunkLength * 2)
            return null;

        const firstChunk = Math.floor(left / rawChunkLength);
        const lastChunk = Math.floor((right - 1) / rawChunkLength);
        for (let index = firstChunk; index <= lastChunk; index++)
            protectedKeys.add(rawChunkKey(source, index));

        for (let index = firstChunk; index <= lastChunk; index++) {
            try {
                requestRawChunk(instance, source, index, protectedKeys).catch(error => {
                    if (!ns.isCancellationError(error)) {
                        ns.reportRuntimeFailure(
                            source.chartId,
                            source.lifecycleEpoch,
                            'WebGPU data generation failed',
                            `${error?.message ?? error} Retry the chart to recreate its GPU resources.`);
                        console.error('[chart-webgpu] raw request failed', error);
                    }
                });
            } catch (error) {
                if (!error?.webGpuCacheCapacity)
                    throw error;
            }
        }

        for (const index of [firstChunk - 1, lastChunk + 1]) {
            if (index < 0 || index >= Math.ceil(source.length / rawChunkLength))
                continue;

            try {
                requestRawChunk(instance, source, index, protectedKeys).catch(error => {
                    if (!ns.isCancellationError(error))
                        console.error('[chart-webgpu] raw prefetch failed', error);
                });
            } catch (error) {
                if (!ns.isCancellationError(error))
                    console.error('[chart-webgpu] raw prefetch skipped', error);
            }
        }

        const items = [];
        for (let index = firstChunk; index <= lastChunk; index++) {
            const chunk = instance.rawChunks.get(rawChunkKey(source, index));
            if (!chunk)
                return null;

            chunk.lastUsed = performance.now();
            const localFirst = Math.max(0, Math.floor(left - chunk.offset));
            const localLast = Math.min(chunk.length - 1, Math.ceil(right - chunk.offset));
            if (localLast <= localFirst)
                continue;

            const zoomInfo = {
                first: localFirst,
                segmentCount: localLast - localFirst,
                zoomedLeft: plot.plotLeft + ((chunk.offset + localFirst - indexLeft) / indexRange) * plot.plotWidth,
                dx: plot.plotWidth / indexRange,
            };
            items.push(ns.getRenderBuffer(instance, chunk, zoomInfo, plot, encoder, `${target}:${source.id}:${index}`, protectedKeys));
        }

        return items.length ? items : null;
    }

    Object.assign(ns, {
        getSyntheticWorker, cancelWorkerRequest, getSeriesKey, destroySeriesBuffer, destroyRawChunk,
        evictRawChunks, removeRawSeries, cancelGeneration, synchronizeSeries, generateSyntheticSeriesAsync,
        destroyChunkedUpload, beginChunkedSeriesAsync, appendChunkedSeriesAsync,
        completeChunkedSeriesAsync, abortChunkedSeries, provideSeriesChunkAsync,
        getSeriesBuffer, getPreviewRenderKey, calculateSeriesRangeAsync, rawChunkKey, requestRawChunk,
        rerenderLastPayloads, getRawRenderItems,
    });
})();
