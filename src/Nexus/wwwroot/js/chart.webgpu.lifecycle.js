(function () {
    const ns = window.__nexusChartWebGpu;
    const {
        shader, decimationShader, rangeShader, overviewShader, pointDecimationShader,
        defaultCacheBudget, reducedPointsPerBucket,
    } = ns;
    const instances = new Map();
    const pendingInstances = new Map();
    const lifecycleEpochs = new Map();
    const failureStates = new Map();
    const dotNetHelpers = new Map();
    const configuredCacheBudgets = new Map();
    let sharedGpu = null;
    let pendingSharedGpu = null;
    let sharedGpuGeneration = 0;

    function valueOf(source, name) {
        if (!source)
            return undefined;

        const camelName = name.charAt(0).toLowerCase() + name.slice(1);
        return source[name] ?? source[camelName];
    }

    function colorOf(source) {
        const color = valueOf(source, 'Color') ?? {};
        return [
            (valueOf(color, 'Red') ?? 0) / 255,
            (valueOf(color, 'Green') ?? 0) / 255,
            (valueOf(color, 'Blue') ?? 0) / 255,
            (valueOf(color, 'Alpha') ?? 255) / 255,
        ];
    }

    function ensureCanvasSize(canvas) {
        const dpr = window.devicePixelRatio || 1;
        const width = Math.max(1, Math.round(canvas.clientWidth * dpr));
        const height = Math.max(1, Math.round(canvas.clientHeight * dpr));

        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }

        return { width, height, dpr };
    }

    function getCanvasContext(instance, target, canvas) {
        const configured = instance.canvasContexts.get(target);

        if (configured?.canvas === canvas)
            return configured.context;

        configured?.context.unconfigure?.();
        instance.previewRenderKeys.delete(target);
        const context = canvas.getContext('webgpu');
        if (!context)
            throw new Error('The browser could not create a WebGPU canvas context.');

        context.configure({
            device: instance.device,
            format: instance.format,
            alphaMode: 'premultiplied',
        });
        instance.canvasContexts.set(target, { canvas, context });
        return context;
    }

    function releaseCanvasContext(instance, target) {
        const configured = instance.canvasContexts.get(target);
        configured?.context.unconfigure?.();
        instance.canvasContexts.delete(target);
        instance.previewRenderKeys.delete(target);
    }

    function getReducedOutputLength(bucketCount) {
        return bucketCount * reducedPointsPerBucket + 2;
    }

    function destroyTrackedBuffer(instance, buffer) {
        if (!buffer || buffer.__nexusDestroyed)
            return;

        buffer.__nexusDestroyed = true;
        instance.ownedGpuBytes -= buffer.__nexusByteLength ?? 0;
        buffer.destroy();
    }

    function ensureGpuCapacity(instance, requiredBytes, protectedRawKeys = new Set(), budget = instance.cacheBudget) {
        ns.evictRawChunks?.(instance, requiredBytes, protectedRawKeys, budget);
        if (instance.ownedGpuBytes + instance.rawReservedBytes + requiredBytes > budget)
            throw new Error(`Chart GPU memory budget (${budget} bytes) cannot fit a ${requiredBytes}-byte allocation`);
    }

    function createTrackedBuffer(instance, descriptor, protectedRawKeys) {
        ensureGpuCapacity(instance, descriptor.size, protectedRawKeys);
        const buffer = instance.device.createBuffer(descriptor);
        buffer.__nexusByteLength = descriptor.size;
        buffer.__nexusDestroyed = false;
        instance.ownedGpuBytes += descriptor.size;
        return buffer;
    }

    function getLifecycleEpoch(chartId) {
        return lifecycleEpochs.get(chartId) ?? 0;
    }

    function advanceLifecycleEpoch(chartId) {
        const epoch = getLifecycleEpoch(chartId) + 1;
        lifecycleEpochs.set(chartId, epoch);
        return epoch;
    }

    function cancellationError(message) {
        const error = new Error(message);
        error.webGpuCancelled = true;
        return error;
    }

    function isCancellationError(error) {
        return error?.webGpuCancelled === true;
    }

    function reportFailure(chartId, title, message) {
        const current = failureStates.get(chartId);
        if (current?.title === title && current?.message === message)
            return;

        failureStates.set(chartId, { title, message });
        const helper = dotNetHelpers.get(chartId);
        helper?.invokeMethodAsync('WebGpuFailed', title, message)
            .catch(error => console.error('[chart-webgpu] failure callback failed', error));
    }

    function reportRuntimeFailure(chartId, epoch, title, message) {
        if (getLifecycleEpoch(chartId) !== epoch || failureStates.has(chartId))
            return;

        advanceLifecycleEpoch(chartId);
        pendingInstances.delete(chartId);
        const instance = instances.get(chartId);
        instances.delete(chartId);

        if (instance)
            destroyInstance(instance, `${title}: ${message}`);

        releaseSharedGpuIfUnused();
        reportFailure(chartId, title, message);
    }

    function destroyInstance(instance, reason) {
        if (!instance || instance.disposed)
            return;

        instance.disposed = true;
        ns.invalidateChartRenders?.(instance.chartId);

        for (const id of [...instance.generationJobs.keys()])
            ns.cancelGeneration(instance, id, reason);

        for (const request of instance.rawRequests.values()) {
            ns.cancelWorkerRequest(instance, request.requestId);
            instance.rawReservedBytes -= request.byteLength;
            request.reject(cancellationError(reason));
        }
        instance.rawRequests.clear();
        instance.syntheticWorker?.terminate();
        instance.syntheticWorker = null;
        instance.workerCallbacks.clear();

        for (const [key, chunk] of instance.rawChunks)
            ns.destroyRawChunk(instance, key, chunk);

        for (const cached of instance.seriesBuffers.values())
            ns.destroySeriesBuffer(instance, cached);
        instance.seriesBuffers.clear();

        for (const upload of instance.chunkedUploadSessions.values())
            ns.destroyChunkedUpload(instance, upload);
        instance.chunkedUploadSessions.clear();

        for (const targetResources of instance.targetResources.values()) {
            for (const resources of targetResources)
                destroyTrackedBuffer(instance, resources.uniformBuffer);
        }
        instance.targetResources.clear();

        for (const { context } of instance.canvasContexts.values())
            context.unconfigure?.();
        instance.canvasContexts.clear();
        instance.lastPayloads.clear();
        instance.previewRenderKeys.clear();

    }

    function releaseSharedGpuIfUnused() {
        if (instances.size > 0 || pendingInstances.size > 0)
            return;

        sharedGpuGeneration++;
        if (!sharedGpu)
            return;

        const gpu = sharedGpu;
        sharedGpu = null;
        gpu.alive = false;
        gpu.device.destroy();
    }

    async function getSharedGpu() {
        if (sharedGpu)
            return sharedGpu;

        if (pendingSharedGpu)
            return pendingSharedGpu;

        const generation = ++sharedGpuGeneration;
        pendingSharedGpu = Promise.resolve().then(async () => {
            if (!navigator.gpu)
                throw new Error('WebGPU is not available. Use a current WebGPU-capable browser and ensure hardware acceleration is enabled.');

            const adapter = await navigator.gpu.requestAdapter();
            if (!adapter)
                throw new Error('No compatible GPU adapter was found. Ensure hardware acceleration is enabled, then retry.');

            const device = await adapter.requestDevice({
                requiredLimits: {
                    maxBufferSize: adapter.limits.maxBufferSize,
                    maxStorageBufferBindingSize: adapter.limits.maxStorageBufferBindingSize,
                },
            });
            let bundle;
            try {
                if (generation !== sharedGpuGeneration)
                    throw new Error('WebGPU initialization was superseded.');

                const format = navigator.gpu.getPreferredCanvasFormat();
                const module = device.createShaderModule({ code: shader });
                const decimationModule = device.createShaderModule({ code: decimationShader });
                const rangeModule = device.createShaderModule({ code: rangeShader });
                const overviewModule = device.createShaderModule({ code: overviewShader });
                const pointDecimationModule = device.createShaderModule({ code: pointDecimationShader });
                bundle = {
                    generation,
                    alive: true,
                    device,
                    format,
                    pipeline: device.createRenderPipeline({
                        layout: 'auto',
                        vertex: { module, entryPoint: 'vertexMain' },
                        fragment: {
                            module,
                            entryPoint: 'fragmentMain',
                            targets: [{
                                format,
                                blend: {
                                    color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha', operation: 'add' },
                                    alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' },
                                },
                            }],
                        },
                        primitive: { topology: 'triangle-list' },
                    }),
                    decimationPipeline: device.createComputePipeline({
                        layout: 'auto', compute: { module: decimationModule, entryPoint: 'decimate' },
                    }),
                    rangePipeline: device.createComputePipeline({
                        layout: 'auto', compute: { module: rangeModule, entryPoint: 'reduceRange' },
                    }),
                    overviewPipeline: device.createComputePipeline({
                        layout: 'auto', compute: { module: overviewModule, entryPoint: 'reduceOverview' },
                    }),
                    pointDecimationPipeline: device.createComputePipeline({
                        layout: 'auto', compute: { module: pointDecimationModule, entryPoint: 'decimatePoints' },
                    }),
                };
            } catch (error) {
                device.destroy();
                throw error;
            }

            device.lost.then(info => {
                bundle.alive = false;

                if (sharedGpu === bundle) {
                    sharedGpu = null;
                    sharedGpuGeneration++;
                }

                const detail = info.message ? ` ${info.message}` : '';
                for (const [chartId, instance] of [...instances]) {
                    if (instance.gpuGeneration !== bundle.generation)
                        continue;

                    advanceLifecycleEpoch(chartId);
                    instances.delete(chartId);
                    destroyInstance(instance, `WebGPU device lost for chart ${chartId}`);
                    reportFailure(chartId, 'GPU connection lost', `The browser lost access to the GPU.${detail} Retry the chart to recreate its GPU resources.`);
                }
            }).catch(error => console.error('[chart-webgpu] device loss handler failed', error));

            sharedGpu = bundle;
            return bundle;
        }).finally(() => {
            pendingSharedGpu = null;
        });

        return pendingSharedGpu;
    }

    async function getInstance(chartId) {
        if (!dotNetHelpers.has(chartId))
            throw cancellationError(`Chart ${chartId} is no longer active`);

        let instance = instances.get(chartId);

        if (instance)
            return instance;

        const failure = failureStates.get(chartId);
        if (failure)
            throw new Error(failure.message);

        let pending = pendingInstances.get(chartId);

        if (pending)
            return pending.promise;

        const epoch = getLifecycleEpoch(chartId);
        const entry = { epoch, promise: null };
        entry.promise = Promise.resolve().then(async () => {
            try {
                if (getLifecycleEpoch(chartId) !== epoch)
                    return null;

                const canvas = document.getElementById(`series_${chartId}`);

                if (!canvas)
                    throw new Error('The chart canvas is unavailable.');

                const gpu = await getSharedGpu();
                if (getLifecycleEpoch(chartId) !== epoch)
                    return null;
                if (!gpu.alive || sharedGpu !== gpu || gpu.generation !== sharedGpuGeneration)
                    throw new Error('The WebGPU device was lost during chart initialization.');

                instance = {
                    chartId,
                    ...gpu,
                    gpuGeneration: gpu.generation,
                    seriesBuffers: new Map(),
                    chunkedUploadSessions: new Map(),
                    uploadToken: 0,
                    targetResources: new Map(),
                    canvasContexts: new Map(),
                    previewRenderKeys: new Map(),
                    uploadGenerations: new Map(),
                    cacheBudget: configuredCacheBudgets.get(chartId) ?? defaultCacheBudget,
                    ownedGpuBytes: 0,
                    rawReservedBytes: 0,
                    rawChunks: new Map(),
                    rawRequests: new Map(),
                    generationJobs: new Map(),
                    workerRequestId: 0,
                    syntheticWorker: null,
                    workerCallbacks: new Map(),
                    lastPayloads: new Map(),
                    disposed: false,
                };

                if (getLifecycleEpoch(chartId) !== epoch) {
                    destroyInstance(instance, `Chart ${chartId} initialization was superseded`);
                    return null;
                }

                instances.set(chartId, instance);
                return instance;
            } catch (error) {
                if (!failureStates.has(chartId) && getLifecycleEpoch(chartId) === epoch) {
                    const unavailable = !navigator.gpu || error?.message?.startsWith('No compatible GPU adapter');
                    reportFailure(
                        chartId,
                        unavailable ? 'WebGPU unavailable' : 'WebGPU initialization failed',
                        unavailable ? error.message : `The chart could not initialize WebGPU: ${error?.message ?? error}. Check browser hardware acceleration, then retry.`);
                }

                throw error;
            } finally {
                if (pendingInstances.get(chartId) === entry)
                    pendingInstances.delete(chartId);

                if (!dotNetHelpers.has(chartId) && !instances.has(chartId))
                    lifecycleEpochs.delete(chartId);
            }
        });

        pendingInstances.set(chartId, entry);
        return entry.promise;
    }

    Object.assign(ns, {
        instances, pendingInstances, lifecycleEpochs, failureStates, dotNetHelpers, configuredCacheBudgets,
        valueOf, colorOf, ensureCanvasSize, getCanvasContext, releaseCanvasContext, getReducedOutputLength,
        createTrackedBuffer, destroyTrackedBuffer, ensureGpuCapacity,
        getLifecycleEpoch, advanceLifecycleEpoch, cancellationError, isCancellationError,
        reportFailure, reportRuntimeFailure, destroyInstance, releaseSharedGpuIfUnused, getSharedGpu, getInstance,
    });
})();
