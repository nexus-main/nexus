(function () {
    const chartGpuVersion = '0.4.0';
    const chartGpuUrl = `https://esm.sh/@chartgpu/chartgpu@${chartGpuVersion}`;
    const wheelZoomInFactor = 0.92;
    const wheelZoomOutFactor = 1 / wheelZoomInFactor;
    const instances = new Map();
    let chartGpuModulePromise;

    function valueOf(source, name) {
        if (!source)
            return undefined;

        const lower = name[0].toLowerCase() + name.slice(1);
        return source[lower] ?? source[name];
    }

    async function loadChartGpu() {
        chartGpuModulePromise ??= import(chartGpuUrl);
        const module = await chartGpuModulePromise;
        return module.ChartGPU ?? module.default ?? module;
    }

    function disposeInstance(id) {
        const instance = instances.get(id);

        if (!instance)
            return;

        instance.resizeObserver?.disconnect();
        instance.abortController?.abort();
        instance.chart?.dispose?.();
        instances.delete(id);
    }

    function createXVector(length, samplePeriodNanoseconds) {
        const x = new Float64Array(length);

        for (let i = 0; i < length; i++)
            x[i] = i * samplePeriodNanoseconds;

        return x;
    }

    function createYVector(values) {
        if (values instanceof Uint8Array) {
            if (values.byteOffset % Float64Array.BYTES_PER_ELEMENT === 0)
                return new Float64Array(values.buffer, values.byteOffset, values.byteLength / Float64Array.BYTES_PER_ELEMENT);

            const copy = values.slice();
            return new Float64Array(copy.buffer, copy.byteOffset, copy.byteLength / Float64Array.BYTES_PER_ELEMENT);
        }

        const y = new Float64Array(values?.length ?? 0);

        for (let i = 0; i < y.length; i++)
            y[i] = values[i];

        return y;
    }

    function toRgba(color, alpha) {
        const match = /^#?([0-9a-f]{6})$/i.exec(color ?? '');

        if (!match)
            return color;

        const value = Number.parseInt(match[1], 16);
        const red = (value >> 16) & 255;
        const green = (value >> 8) & 255;
        const blue = value & 255;
        return `rgba(${red}, ${green}, ${blue}, ${alpha})`;
    }

    function createZeroCrossingSafeAreaData(x, y) {
        let extraPoints = 0;

        for (let i = 1; i < y.length; i++) {
            const previous = y[i - 1];
            const current = y[i];

            if (Number.isFinite(previous) && Number.isFinite(current) && previous !== 0 && current !== 0 && Math.sign(previous) !== Math.sign(current))
                extraPoints++;
        }

        if (extraPoints === 0)
            return { x, y };

        const filledX = new Float64Array(x.length + extraPoints);
        const filledY = new Float64Array(y.length + extraPoints);
        let target = 0;

        filledX[target] = x[0];
        filledY[target] = y[0];
        target++;

        for (let i = 1; i < y.length; i++) {
            const previous = y[i - 1];
            const current = y[i];

            if (Number.isFinite(previous) && Number.isFinite(current) && previous !== 0 && current !== 0 && Math.sign(previous) !== Math.sign(current)) {
                const t = -previous / (current - previous);
                filledX[target] = x[i - 1] + (x[i] - x[i - 1]) * t;
                filledY[target] = 0;
                target++;
            }

            filledX[target] = x[i];
            filledY[target] = current;
            target++;
        }

        return { x: filledX, y: filledY };
    }

    function finiteMinMax(values, beginAtZero) {
        let min = beginAtZero ? 0 : Number.POSITIVE_INFINITY;
        let max = Number.NEGATIVE_INFINITY;

        for (const value of values) {
            if (!Number.isFinite(value))
                continue;

            min = Math.min(min, value);
            max = Math.max(max, value);
        }

        if (!Number.isFinite(min) || !Number.isFinite(max))
            return { min: 0, max: 1 };

        if (min === max) {
            const padding = Math.max(1, Math.abs(min) * 0.1);
            min -= padding;
            max += padding;
        }

        const padding = (max - min) * 0.05;
        return { min: min - padding, max: max + padding };
    }

    function formatNumber(value) {
        if (!Number.isFinite(value))
            return '--';

        const abs = Math.abs(value);

        if (abs >= 1000 || (abs > 0 && abs < 0.001))
            return value.toExponential(3);

        return value.toLocaleString(undefined, { maximumFractionDigits: 6 });
    }

    function nsToDate(originUnixNanoseconds, offsetNanoseconds) {
        const absolute = originUnixNanoseconds + BigInt(Math.round(offsetNanoseconds));
        return new Date(Number(absolute / 1000000n));
    }

    function formatTimestamp(originUnixNanoseconds, offsetNanoseconds, visibleSpanNanoseconds) {
        const date = nsToDate(originUnixNanoseconds, offsetNanoseconds);

        if (visibleSpanNanoseconds < 1000)
            return `${date.toISOString()} +${Math.round(offsetNanoseconds)}ns`;

        if (visibleSpanNanoseconds < 1000000)
            return `${date.toISOString()} +${Math.round(offsetNanoseconds % 1000000)}ns`;

        if (visibleSpanNanoseconds < 1000000000)
            return date.toISOString().replace('Z', '');

        if (visibleSpanNanoseconds < 86400000000000)
            return date.toISOString().replace('T', ' ').replace('Z', '');

        if (visibleSpanNanoseconds < 31536000000000000)
            return date.toISOString().slice(0, 10);

        return date.toISOString().slice(0, 7);
    }

    function buildLineModel(payload) {
        const originUnixNanoseconds = BigInt(valueOf(payload, 'OriginUnixNanoseconds'));
        const durationNanoseconds = Number(valueOf(payload, 'DurationNanoseconds') ?? 0);
        const beginAtZero = Boolean(valueOf(payload, 'BeginAtZero'));
        const inputSeries = valueOf(payload, 'Series') ?? [];
        const units = [...new Set(inputSeries.map(series => valueOf(series, 'Unit') || ''))];
        const yDomains = new Map();

        const axes = units.map((unit, index) => {
            const values = inputSeries
                .filter(series => (valueOf(series, 'Unit') || '') === unit)
                .flatMap(series => Array.from(createYVector(valueOf(series, 'ValuesBytes') ?? valueOf(series, 'Values') ?? [])));
            const domain = finiteMinMax(values, beginAtZero);
            const id = `y-${index}`;
            yDomains.set(id, { ...domain, originalMin: domain.min, originalMax: domain.max });

            return {
                id,
                type: 'value',
                position: 'left',
                offset: index * 56,
                name: unit || 'Value',
                header: unit || 'Value',
                min: domain.min,
                max: domain.max,
                tickFormatter: formatNumber
            };
        });

        const series = inputSeries.map(input => {
            const unit = valueOf(input, 'Unit') || '';
            const axisIndex = Math.max(0, units.indexOf(unit));
            const values = valueOf(input, 'ValuesBytes') ?? valueOf(input, 'Values') ?? [];
            const samplePeriodNanoseconds = Number(valueOf(input, 'SamplePeriodNanoseconds') ?? 1);
            const y = createYVector(values);
            const x = createXVector(y.length, samplePeriodNanoseconds);
            const color = valueOf(input, 'Color');

            return {
                type: 'line',
                name: valueOf(input, 'Name'),
                color,
                yAxis: `y-${axisIndex}`,
                data: createZeroCrossingSafeAreaData(x, y),
                baseline: 0,
                lineWidth: 2,
                strokeWidth: 1,
                stroke: color,
                strokeColor: color,
                fill: toRgba(color, 0.35),
                fillColor: toRgba(color, 0.35),
                fillOpacity: 0.35,
                areaStyle: { color: toRgba(color, 0.35), opacity: 0.35 },
                lineStyle: { color, width: 2 },
                connectNulls: false,
                showSymbol: false
            };
        });

        return { originUnixNanoseconds, durationNanoseconds, axes, yDomains, series };
    }

    function applyVerticalZoom(instance, event) {
        const direction = event.deltaY < 0 ? -1 : 1;
        const factor = direction < 0 ? wheelZoomInFactor : wheelZoomOutFactor;
        const rect = instance.element.getBoundingClientRect();
        const anchor = rect.height > 0 ? 1 - ((event.clientY - rect.top) / rect.height) : 0.5;

        for (const domain of instance.yDomains.values()) {
            const range = domain.max - domain.min;
            const nextRange = range * factor;
            const valueAtAnchor = domain.min + range * anchor;
            domain.min = valueAtAnchor - nextRange * anchor;
            domain.max = valueAtAnchor + nextRange * (1 - anchor);
        }

        for (const axis of instance.options.axes.y) {
            const domain = instance.yDomains.get(axis.id);
            axis.min = domain.min;
            axis.max = domain.max;
        }

        instance.chart.setOption(instance.options);
    }

    function applyHorizontalZoom(instance, event) {
        const current = instance.chart.getZoomRange?.() ?? { start: 0, end: 100 };
        const start = current.start ?? 0;
        const end = current.end ?? 100;
        const span = end - start;
        const factor = event.deltaY < 0 ? wheelZoomInFactor : wheelZoomOutFactor;
        const rect = instance.element.getBoundingClientRect();
        const anchor = rect.width > 0 ? (event.clientX - rect.left) / rect.width : 0.5;
        const nextSpan = Math.max(0.000001, Math.min(100, span * factor));
        let nextStart = start + (span - nextSpan) * anchor;
        let nextEnd = nextStart + nextSpan;

        if (nextStart < 0) {
            nextEnd -= nextStart;
            nextStart = 0;
        }

        if (nextEnd > 100) {
            nextStart -= nextEnd - 100;
            nextEnd = 100;
        }

        instance.chart.setZoomRange(Math.max(0, nextStart), Math.min(100, nextEnd), 'nexus-wheel');
    }

    function resetLineZoom(instance) {
        instance.chart.setZoomRange?.(0, 100, 'nexus-double-click');

        for (const domain of instance.yDomains.values()) {
            domain.min = domain.originalMin;
            domain.max = domain.originalMax;
        }

        for (const axis of instance.options.axes.y) {
            const domain = instance.yDomains.get(axis.id);
            axis.min = domain.min;
            axis.max = domain.max;
        }

        instance.chart.setOption(instance.options);
    }

    function installWheelZoom(instance) {
        instance.element.addEventListener('wheel', event => {
            event.preventDefault();
            event.stopImmediatePropagation();

            const zoomHorizontal = event.altKey || (!event.shiftKey && !event.altKey);
            const zoomVertical = event.shiftKey || (!event.shiftKey && !event.altKey);

            if (zoomHorizontal)
                applyHorizontalZoom(instance, event);

            if (zoomVertical)
                applyVerticalZoom(instance, event);
        }, { capture: true, passive: false, signal: instance.abortController.signal });

        instance.element.addEventListener('dblclick', event => {
            event.preventDefault();
            resetLineZoom(instance);
        }, { passive: false, signal: instance.abortController.signal });
    }

    async function createOrUpdateLineChart(id, payload) {
        disposeInstance(id);

        const element = document.getElementById(`chart_${id}`);
        if (!element)
            return;

        const ChartGPU = await loadChartGpu();
        const model = buildLineModel(payload);
        let visibleSpanNanoseconds = model.durationNanoseconds;

        const options = {
            animation: false,
            theme: 'light',
            grid: { left: 72 + model.axes.length * 56 },
            legend: { show: true, position: 'top' },
            xAxis: {
                type: 'value',
                min: 0,
                max: model.durationNanoseconds,
                name: 'Time',
                tickFormatter: value => formatTimestamp(model.originUnixNanoseconds, value, visibleSpanNanoseconds)
            },
            axes: { y: model.axes },
            dataZoom: [
                { type: 'inside', start: 0, end: 100, minSpan: 0.000001 },
                { type: 'slider' },
            ],
            tooltip: {
                show: true,
                trigger: 'axis',
                formatter: params => {
                    const rows = Array.isArray(params) ? params : [params];
                    const x = rows[0]?.value?.[0] ?? 0;
                    return [formatTimestamp(model.originUnixNanoseconds, x, visibleSpanNanoseconds)]
                        .concat(rows.map(row => `${row.seriesName}: ${formatNumber(row.value?.[1])}`))
                        .join('<br>');
                }
            },
            series: model.series
        };

        const chart = await ChartGPU.create(element, options);
        const abortController = new AbortController();
        const resizeObserver = new ResizeObserver(() => chart.resize?.());
        const instance = { element, chart, options, yDomains: model.yDomains, abortController, resizeObserver };
        instances.set(id, instance);

        chart.on?.('zoomRangeChange', range => {
            const start = range?.start ?? 0;
            const end = range?.end ?? 100;
            visibleSpanNanoseconds = model.durationNanoseconds * Math.max(0, end - start) / 100;
        });

        resizeObserver.observe(element);
        installWheelZoom(instance);
    }

    function buildAvailabilitySeries(payload) {
        const values = valueOf(payload, 'ValuesBytes') ?? valueOf(payload, 'Values') ?? [];
        const stepNanoseconds = Number(valueOf(payload, 'StepNanoseconds') ?? 1);
        const y = createYVector(values);
        const x = createXVector(y.length, stepNanoseconds);

        return { x, y };
    }

    async function createOrUpdateAvailabilityChart(id, payload) {
        disposeInstance(id);

        const element = document.getElementById(`availability-chart_${id}`);
        if (!element)
            return;

        const ChartGPU = await loadChartGpu();
        const originUnixNanoseconds = BigInt(valueOf(payload, 'OriginUnixNanoseconds'));
        const data = buildAvailabilitySeries(payload);
        const maxX = data.x.length === 0 ? 1 : data.x[data.x.length - 1];

        const options = {
            animation: false,
            legend: { show: false },
            xAxis: {
                type: 'value',
                min: 0,
                max: maxX,
                tickFormatter: value => formatTimestamp(originUnixNanoseconds, value, maxX)
            },
            yAxis: {
                type: 'value',
                min: 0,
                max: 1,
                name: 'Availability / %',
                tickFormatter: value => Math.round(value * 100).toString()
            },
            tooltip: {
                show: true,
                trigger: 'axis',
                formatter: params => {
                    const row = Array.isArray(params) ? params[0] : params;
                    return `${formatTimestamp(originUnixNanoseconds, row.value[0], maxX)}<br>Availability: ${formatNumber(row.value[1] * 100)}%`;
                }
            },
            series: [{
                type: 'bar',
                name: 'Availability',
                color: '#f97316',
                data
            }]
        };

        const chart = await ChartGPU.create(element, options);
        const resizeObserver = new ResizeObserver(() => chart.resize?.());
        instances.set(id, { element, chart, options, yDomains: new Map(), resizeObserver });
        resizeObserver.observe(element);
    }

    window.nexus ??= {};
    window.nexus.chartGpu = {
        createOrUpdateLineChart,
        createOrUpdateAvailabilityChart,
        dispose: disposeInstance
    };
})();
