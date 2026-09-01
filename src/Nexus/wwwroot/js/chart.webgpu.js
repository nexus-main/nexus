(function () {
    const ns = window.__nexusChartWebGpu;
    const {
        instances, pendingInstances, lifecycleEpochs, failureStates, dotNetHelpers, configuredCacheBudgets,
        valueOf, colorOf, ensureCanvasSize, getCanvasContext, releaseCanvasContext, getReducedOutputLength,
        getSharedGpu, getInstance, getLifecycleEpoch, advanceLifecycleEpoch, isCancellationError,
        reportRuntimeFailure, destroyInstance, getSyntheticWorker, evictRawChunks,
        createTrackedBuffer, destroyTrackedBuffer, ensureGpuCapacity,
        synchronizeSeries, appendSeriesUploadAsync, completeSeriesUploadAsync, abortSeriesUpload,
        generateSyntheticSeriesAsync, getSeriesBuffer, getPreviewRenderKey, getRawRenderItems, createSeriesUpload,
        uniformBufferSize, fillVerticesPerSegment, lineVerticesPerSegment, decimationFactor,
        decimationBucketsPerPixel, maxDecimationBuckets, overviewBucketSize, reducedPointsPerBucket,
    } = ns;
    const renderStates = new Map();

    function renderStateKey(chartId, target) {
        return `${chartId}:${target}`;
    }

    function invalidateChartRenders(chartId) {
        for (const [key, state] of renderStates) {
            if (!key.startsWith(`${chartId}:`))
                continue;
            state.generation++;
            state.pending = null;
            renderStates.delete(key);
        }
    }

    function destroyTargetDecimations(instance, target) {
        for (const source of [...instance.seriesBuffers.values(), ...instance.rawChunks.values()]) {
            for (const [key, decimation] of source.decimations ?? []) {
                if (key !== target && !key.startsWith(`${target}:`))
                    continue;
                destroyTrackedBuffer(instance, decimation.outputBuffer);
                destroyTrackedBuffer(instance, decimation.paramsBuffer);
                instance.auxiliaryBytes -= decimation.byteLength;
                source.decimations.delete(key);
            }
        }
    }

    function releaseTarget(chartId, target) {
        const key = renderStateKey(chartId, target);
        const state = renderStates.get(key);
        if (state) {
            state.generation++;
            state.pending = null;
            renderStates.delete(key);
        }

        const instance = instances.get(chartId);
        if (!instance)
            return;

        instance.lastPayloads.delete(target);
        instance.previewRenderKeys.delete(target);
        trimDrawResources(instance, target, 0);
        releaseCanvasContext(instance, target);
        destroyTargetDecimations(instance, target);
    }

    function getDrawResources(instance, seriesBuffer, drawIndex, target, protectedRawKeys) {
        let targetResources = instance.targetResources.get(target);

        if (!targetResources) {
            targetResources = [];
            instance.targetResources.set(target, targetResources);
        }

        let resources = targetResources[drawIndex];

        if (resources?.seriesBuffer === seriesBuffer)
            return resources;

        destroyTrackedBuffer(instance, resources?.uniformBuffer);

        const uniformBuffer = createTrackedBuffer(instance, {
            size: uniformBufferSize,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        }, protectedRawKeys);
        const bindGroup = instance.device.createBindGroup({
            layout: instance.pipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: { buffer: uniformBuffer } },
                { binding: 1, resource: { buffer: seriesBuffer.buffer } },
                { binding: 2, resource: { buffer: seriesBuffer.pointBuffer } },
            ],
        });

        resources = { seriesBuffer, uniformBuffer, bindGroup };
        targetResources[drawIndex] = resources;
        return resources;
    }

    function trimDrawResources(instance, target, count) {
        const resources = instance.targetResources.get(target);

        if (!resources || resources.length <= count)
            return;

        for (const stale of resources.splice(count))
            destroyTrackedBuffer(instance, stale.uniformBuffer);

        if (resources.length === 0)
            instance.targetResources.delete(target);
    }

    function getPlot(payload, width, height) {
        const plot = valueOf(payload, 'Plot') ?? {};
        const plotLeft = (valueOf(plot, 'Left') ?? 0) * width;
        const plotTop = (valueOf(plot, 'Top') ?? 0) * height;
        const plotRight = (valueOf(plot, 'Right') ?? 1) * width;
        const plotBottom = (valueOf(plot, 'Bottom') ?? 1) * height;
        const plotWidth = plotRight - plotLeft;
        const plotHeight = plotBottom - plotTop;

        if (plotWidth <= 0 || plotHeight <= 0)
            return null;

        return { plotLeft, plotTop, plotRight, plotBottom, plotWidth, plotHeight };
    }

    function getTimeWindow(payload, series, length) {
        const zoom = valueOf(payload, 'Zoom') ?? {};
        const zoomLeft = valueOf(zoom, 'Left') ?? 0;
        const zoomRight = valueOf(zoom, 'Right') ?? 1;
        const sampleStep = valueOf(series, 'SampleStep');
        const indexLeft = zoomLeft / sampleStep;
        const indexRight = zoomRight / sampleStep;
        const indexRange = indexRight - indexLeft;

        if (!Number.isFinite(sampleStep) || sampleStep <= 0 ||
            !Number.isFinite(indexRange) || indexRange <= 0)
            return null;

        const first = Math.max(0, Math.floor(indexLeft));
        const last = Math.min(length - 1, Math.ceil(indexRight));

        if (last < first)
            return null;

        return { first, last, indexLeft, indexRange };
    }

    function getZoomInfo(payload, series, length, plot) {
        const timeWindow = getTimeWindow(payload, series, length);

        if (!timeWindow)
            return null;

        const { first, last, indexLeft, indexRange } = timeWindow;
        const visibleLength = last - first + 1;
        const zoomedLeft = plot.plotLeft + ((first - indexLeft) / indexRange) * plot.plotWidth;
        const dx = plot.plotWidth / indexRange;

        if (visibleLength < 2 || !Number.isFinite(dx) || dx <= 0)
            return null;

        return { first, segmentCount: visibleLength - 1, zoomedLeft, dx };
    }

    function getSyntheticOverviewZoom(payload, series, source, plot) {
        const timeWindow = getTimeWindow(payload, series, source.length);

        if (!timeWindow)
            return null;

        const { first: left, last: right, indexLeft, indexRange } = timeWindow;

        const firstBucket = Math.max(0, Math.floor(left / overviewBucketSize) - 1);
        const lastBucket = Math.min(source.overviewBucketCount - 1, Math.ceil(right / overviewBucketSize));
        const first = firstBucket * reducedPointsPerBucket;
        const visibleLength = (lastBucket - firstBucket + 1) * reducedPointsPerBucket;

        if (visibleLength < 2)
            return null;

        return {
            first,
            segmentCount: visibleLength - 1,
            zoomedLeft: plot.plotLeft,
            dx: plot.plotWidth / (indexRange / overviewBucketSize),
            xOrigin: indexLeft / overviewBucketSize,
        };
    }

    function getRenderBuffer(instance, source, zoomInfo, plot, encoder, target, protectedRawKeys) {
        const visibleLength = zoomInfo.segmentCount + 1;
        const bucketCount = Math.min(maxDecimationBuckets, Math.max(2, Math.ceil(plot.plotWidth * decimationBucketsPerPixel)));

        if (visibleLength <= bucketCount * decimationFactor)
            return { seriesBuffer: source, zoomInfo };

        let decimation = source.decimations.get(target);
        const outputLength = getReducedOutputLength(bucketCount);
        const outputSize = outputLength * 2 * Float32Array.BYTES_PER_ELEMENT;

        if (!decimation || decimation.bucketCount !== bucketCount) {
            if (decimation) {
                destroyTrackedBuffer(instance, decimation.outputBuffer);
                destroyTrackedBuffer(instance, decimation.paramsBuffer);
                instance.auxiliaryBytes -= decimation.byteLength;
            }

            const allocationBytes = outputSize + 16;
            let outputBuffer = null;
            let paramsBuffer = null;
            try {
                outputBuffer = createTrackedBuffer(instance, {
                    size: outputSize,
                    usage: GPUBufferUsage.STORAGE,
                }, protectedRawKeys);
                paramsBuffer = createTrackedBuffer(instance, {
                    size: 16,
                    usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
                }, protectedRawKeys);
            } catch (error) {
                destroyTrackedBuffer(instance, outputBuffer);
                destroyTrackedBuffer(instance, paramsBuffer);
                throw error;
            }
            const bindGroup = instance.device.createBindGroup({
                layout: (source.dataMode === 1 ? instance.pointDecimationPipeline : instance.decimationPipeline).getBindGroupLayout(0),
                entries: [
                    { binding: 0, resource: { buffer: source.dataMode === 1 ? source.pointBuffer : source.buffer } },
                    { binding: 1, resource: { buffer: outputBuffer } },
                    { binding: 2, resource: { buffer: paramsBuffer } },
                ],
            });

            decimation = {
                bucketCount,
                outputBuffer,
                paramsBuffer,
                bindGroup,
                byteLength: allocationBytes,
                renderBuffer: {
                    buffer: source.buffer,
                    pointBuffer: outputBuffer,
                    length: outputLength,
                    dataMode: 1,
                },
            };
            source.decimations.set(target, decimation);
            instance.auxiliaryBytes += allocationBytes;
        }

        instance.device.queue.writeBuffer(
            decimation.paramsBuffer,
            0,
            new Uint32Array([zoomInfo.first, visibleLength, bucketCount, source.overviewLength ?? source.length]));

        const pass = encoder.beginComputePass();
        pass.setPipeline(source.dataMode === 1 ? instance.pointDecimationPipeline : instance.decimationPipeline);
        pass.setBindGroup(0, decimation.bindGroup);
        pass.dispatchWorkgroups(bucketCount);
        pass.end();

        return {
            seriesBuffer: decimation.renderBuffer,
            zoomInfo: {
                first: 0,
                segmentCount: outputLength - 1,
                zoomedLeft: zoomInfo.zoomedLeft,
                dx: zoomInfo.dx,
                xOrigin: zoomInfo.xOrigin ?? 0,
            },
        };
    }

    function writeUniforms(instance, uniformBuffer, seriesBuffer, payload, series, plot, zoomInfo, width, height, dpr, mode) {
        const preview = valueOf(payload, 'Preview') ?? false;
        const axisMin = preview ? (valueOf(series, 'OverviewAxisMin') ?? 0) : (valueOf(series, 'AxisMin') ?? 0);
        const axisMax = preview ? (valueOf(series, 'OverviewAxisMax') ?? 1) : (valueOf(series, 'AxisMax') ?? 1);
        const axisRange = axisMax - axisMin;

        if (!Number.isFinite(axisRange) || axisRange === 0)
            return false;

        const lineWidth = (valueOf(payload, 'LineWidth') ?? 0.7) * dpr;
        const fillOpacity = valueOf(payload, 'FillOpacity') ?? 0.10;
        const zeroY = Math.min(plot.plotBottom, Math.max(plot.plotTop, plot.plotBottom - (0 - axisMin) / axisRange * plot.plotHeight));
        const color = colorOf(series);
        const data = new ArrayBuffer(uniformBufferSize);
        const floats = new Float32Array(data);
        const uints = new Uint32Array(data);

        floats[0] = width;
        floats[1] = height;
        floats[4] = plot.plotLeft;
        floats[5] = plot.plotTop;
        floats[6] = plot.plotRight;
        floats[7] = plot.plotBottom;
        floats[8] = axisMin;
        floats[9] = axisRange;
        floats[10] = zoomInfo.zoomedLeft;
        floats[11] = zoomInfo.dx;
        floats[12] = zeroY;
        floats[13] = lineWidth;
        floats[14] = fillOpacity;
        floats[16] = color[0];
        floats[17] = color[1];
        floats[18] = color[2];
        floats[19] = color[3];
        uints[20] = zoomInfo.first;
        uints[21] = mode;
        uints[22] = seriesBuffer.dataMode ?? 0;
        floats[23] = zoomInfo.xOrigin ?? 0;

        instance.device.queue.writeBuffer(uniformBuffer, 0, data);
        return true;
    }

    async function beginSeriesUploadAsync(chartId, id, version, length) {
        const instance = await getInstance(chartId);

        if (!instance)
            throw new Error(`WebGPU instance unavailable for chart ${chartId}`);

        return createSeriesUpload(instance, id, version, length);
    }

    async function runRuntimeOperation(chartId, title, action) {
        const epoch = getLifecycleEpoch(chartId);

        try {
            return await action();
        } catch (error) {
            if (!isCancellationError(error)) {
                reportRuntimeFailure(
                    chartId,
                    epoch,
                    title,
                    `${error?.message ?? error} Retry the chart to recreate its GPU resources.`);
            }

            throw error;
        }
    }

    async function renderSeriesAsync(chartId, payload, renderState = null, renderGeneration = 0) {
        const instance = await getInstance(chartId);

        if (!instance)
            return;

        const { device, format, pipeline } = instance;
        const target = valueOf(payload, 'Target') ?? 'series';
        if (renderState && (renderState.generation !== renderGeneration || renderStates.get(renderStateKey(chartId, target)) !== renderState))
            throw ns.cancellationError(`Rendering target ${target} was superseded`);
        const canvas = document.getElementById(`${target}_${chartId}`);

        if (!canvas) {
            releaseCanvasContext(instance, target);
            return;
        }

        const context = getCanvasContext(instance, target, canvas);
        const { width, height, dpr } = ensureCanvasSize(canvas);
        const isPreview = valueOf(payload, 'Preview') ?? false;
        instance.lastPayloads.set(target, payload);
        let previewRenderKey = null;

        if (isPreview) {
            previewRenderKey = getPreviewRenderKey(instance, payload, width, height);

            if (instance.previewRenderKeys.get(target) === previewRenderKey)
                return;
        }

        const plot = getPlot(payload, width, height);

        const encoder = device.createCommandEncoder();
        const renderItems = [];
        const protectedRawKeys = new Set();
        let drawResourceCount = 0;

        if (plot) {
            const seriesList = valueOf(payload, 'Series') ?? [];

            for (const series of seriesList) {
                const cached = getSeriesBuffer(instance, series);

                if (!cached)
                    continue;

                cached.chartId = chartId;
                cached.generation = instance.uploadGenerations.get(cached.id);
                cached.lifecycleEpoch = getLifecycleEpoch(chartId);
                if (cached.synthetic) {
                    const rawItems = getRawRenderItems(instance, cached, series, payload, plot, encoder, target, protectedRawKeys);
                    if (rawItems) {
                        for (const rawItem of rawItems)
                            renderItems.push({ series, ...rawItem });
                        continue;
                    }
                }

                const zoomInfo = cached.synthetic
                    ? getSyntheticOverviewZoom(payload, series, cached, plot)
                    : getZoomInfo(payload, series, cached.length, plot);

                if (!zoomInfo)
                    continue;

                const renderItem = getRenderBuffer(instance, cached, zoomInfo, plot, encoder, target, protectedRawKeys);
                renderItems.push({ series, ...renderItem });
            }
        }

        const pass = encoder.beginRenderPass({
            colorAttachments: [{
                view: context.getCurrentTexture().createView(),
                loadOp: 'clear',
                storeOp: 'store',
                clearValue: { r: 0, g: 0, b: 0, a: 0 },
            }],
        });

        if (plot) {
            pass.setPipeline(pipeline);
            pass.setScissorRect(
                Math.max(0, Math.floor(plot.plotLeft)),
                Math.max(0, Math.floor(plot.plotTop)),
                Math.max(1, Math.ceil(plot.plotWidth)),
                Math.max(1, Math.ceil(plot.plotHeight)));

            for (const { series, seriesBuffer, zoomInfo } of renderItems) {
                const fillResources = getDrawResources(instance, seriesBuffer, drawResourceCount++, target, protectedRawKeys);

                if (writeUniforms(instance, fillResources.uniformBuffer, seriesBuffer, payload, series, plot, zoomInfo, width, height, dpr, 0)) {
                    pass.setBindGroup(0, fillResources.bindGroup);
                    pass.draw(zoomInfo.segmentCount * fillVerticesPerSegment);
                }
            }

            for (const { series, seriesBuffer, zoomInfo } of renderItems) {
                const lineResources = getDrawResources(instance, seriesBuffer, drawResourceCount++, target, protectedRawKeys);

                if (writeUniforms(instance, lineResources.uniformBuffer, seriesBuffer, payload, series, plot, zoomInfo, width, height, dpr, 1)) {
                    pass.setBindGroup(0, lineResources.bindGroup);
                    pass.draw(zoomInfo.segmentCount * lineVerticesPerSegment);
                }
            }
        }

        pass.end();
        device.queue.submit([encoder.finish()]);
        trimDrawResources(instance, target, drawResourceCount);

        if (previewRenderKey !== null)
            instance.previewRenderKeys.set(target, previewRenderKey);

    }

    function scheduleRender(chartId, payload) {
        const target = valueOf(payload, 'Target') ?? 'series';
        const key = renderStateKey(chartId, target);
        let state = renderStates.get(key);
        if (!state) {
            state = { generation: 0, pending: null, running: false };
            renderStates.set(key, state);
        }
        state.pending = payload;
        if (state.running)
            return state.promise;

        state.running = true;
        const generation = state.generation;
        state.promise = (async () => {
            try {
                while (state.pending && state.generation === generation) {
                    const next = state.pending;
                    state.pending = null;
                    await runRuntimeOperation(chartId, 'WebGPU rendering failed', () =>
                        renderSeriesAsync(chartId, next, state, generation));
                }
            } finally {
                state.running = false;
                if (renderStates.get(key) === state && !state.pending)
                    renderStates.delete(key);
            }
        })();
        return state.promise;
    }

    Object.assign(ns, {
        getRenderBuffer, getTimeWindow, runRuntimeOperation, renderSeriesAsync, scheduleRender,
        releaseTarget, invalidateChartRenders,
    });

    window.nexus ??= {};
    window.nexus.chartWebGpu = {
        initialize(chartId, dotNetHelper) {
            dotNetHelpers.set(chartId, dotNetHelper);

            const failure = failureStates.get(chartId);
            if (failure) {
                dotNetHelper.invokeMethodAsync('WebGpuFailed', failure.title, failure.message)
                    .catch(error => console.error('[chart-webgpu] failure callback failed', error));
                return;
            }

            getInstance(chartId).catch(error => console.error('[chart-webgpu] initialization failed', error));
        },
        setCacheBudget(chartId, bytes) {
            if (!Number.isSafeInteger(bytes) || bytes < 0)
                throw new Error(`Cache budget must be a non-negative safe integer (received ${bytes})`);

            const instance = instances.get(chartId);
            if (instance) {
                evictRawChunks(instance, 0, new Set(), bytes);
                if (instance.ownedGpuBytes + instance.rawReservedBytes > bytes)
                    throw new Error(`Chart GPU memory budget cannot be lowered below its ${instance.ownedGpuBytes + instance.rawReservedBytes} bytes of active allocations`);
                instance.cacheBudget = bytes;
            }

            configuredCacheBudgets.set(chartId, bytes);
        },
        synchronizeSeries(chartId, activeIds) {
            const instance = instances.get(chartId);
            if (instance)
                synchronizeSeries(instance, activeIds);
        },
        beginSeriesUpload(chartId, id, version, length) {
            return runRuntimeOperation(chartId, 'WebGPU upload failed', () =>
                beginSeriesUploadAsync(chartId, id, version, length));
        },
        appendSeriesUpload(chartId, token, byteOffset, streamReference) {
            return runRuntimeOperation(chartId, 'WebGPU upload failed', () =>
                appendSeriesUploadAsync(chartId, token, byteOffset, streamReference));
        },
        completeSeriesUpload(chartId, token) {
            return runRuntimeOperation(chartId, 'WebGPU upload failed', () =>
                completeSeriesUploadAsync(chartId, token));
        },
        abortSeriesUpload(chartId, token) {
            abortSeriesUpload(chartId, token);
        },
        generateSyntheticSeries(chartId, id, version, length, kind) {
            return runRuntimeOperation(chartId, 'WebGPU data generation failed', () =>
                generateSyntheticSeriesAsync(chartId, id, version, length, kind));
        },
        renderSeries(chartId, payload) {
            scheduleRender(chartId, payload)
                .catch(error => {
                    if (!isCancellationError(error))
                        console.error('[chart-webgpu] render failed', error);
                });
        },
        releaseTarget,
        async retry(chartId) {
            advanceLifecycleEpoch(chartId);
            pendingInstances.delete(chartId);

            const instance = instances.get(chartId);
            if (instance) {
                instances.delete(chartId);
                destroyInstance(instance, `Chart ${chartId} WebGPU retry`);
            }

            failureStates.delete(chartId);
            return (await getInstance(chartId)) !== null;
        },
        dispose(chartId) {
            advanceLifecycleEpoch(chartId);
            pendingInstances.delete(chartId);

            const instance = instances.get(chartId);
            instances.delete(chartId);

            if (instance)
                destroyInstance(instance, `Chart ${chartId} was disposed`);

            configuredCacheBudgets.delete(chartId);
            failureStates.delete(chartId);
            dotNetHelpers.delete(chartId);
        },
    };

    if (globalThis.__nexusChartWebGpuTestHooks) {
        Object.assign(globalThis.__nexusChartWebGpuTestHooks, {
            instances,
            pendingInstances,
            lifecycleEpochs,
            failureStates,
            getSharedGpu,
            getInstance,
            runRuntimeOperation,
            evictRawChunks,
            trimDrawResources,
            colorOf,
            getCanvasContext,
            releaseCanvasContext,
            getReducedOutputLength,
            reducedPointsPerBucket,
            getSyntheticWorker,
            getTimeWindow,
            getZoomInfo,
            getSyntheticOverviewZoom,
            getRawRenderItems,
            scheduleRender,
            releaseTarget,
            renderStates,
            createTrackedBuffer,
            destroyTrackedBuffer,
        });
    }
})();
