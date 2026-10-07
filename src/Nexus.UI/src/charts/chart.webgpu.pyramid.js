(function () {
  const ns = window.__nexusChartWebGpu;
  const summaryBytes = 48;
  const summaryWgsl = `
struct Summary {
    start: u32, count: u32, first: f32, last: f32,
    minimum: f32, maximum: f32, minIndex: u32, maxIndex: u32,
    gapIndex: u32, gaps: u32, finite: u32, pad: u32,
};
fn invalid(v: f32) -> bool { return (bitcast<u32>(v) & 0x7f800000u) == 0x7f800000u; }
fn sample(index: u32, value: f32) -> Summary {
    let gap = select(0u, 1u, invalid(value));
    return Summary(index, 1u, value, value, value, value, index, index, index, gap, 1u-gap, 0u);
}
fn merge(a: Summary, b: Summary) -> Summary {
    if (a.count == 0u) { return b; }
    if (b.count == 0u) { return a; }
    var s = a;
    s.count += b.count;
    s.last = b.last;
    if (b.finite != 0u) {
        if (a.finite == 0u || b.minimum < a.minimum) { s.minimum = b.minimum; s.minIndex = b.minIndex; }
        if (a.finite == 0u || b.maximum > a.maximum) { s.maximum = b.maximum; s.maxIndex = b.maxIndex; }
    }
    if (a.gaps == 0u) { s.gapIndex = b.gapIndex; }
    s.gaps = min(2u, a.gaps + b.gaps - select(0u, 1u, invalid(a.last) && invalid(b.first)));
    s.finite |= b.finite;
    return s;
}
`;
  const baseSummaryShader =
    summaryWgsl +
    `
struct Params { offset: u32, count: u32, phase: u32, firstBucket: u32, pageFirst: u32, pad0: u32, pad1: u32, pad2: u32 };
@group(0) @binding(0) var<storage, read> raw: array<f32>;
@group(0) @binding(1) var<storage, read_write> summaries: array<Summary>;
@group(0) @binding(2) var<uniform> p: Params;
var<workgroup> lanes: array<Summary, 256>;
@compute @workgroup_size(256)
fn base(@builtin(workgroup_id) group: vec3u, @builtin(local_invocation_id) local: vec3u) {
    let bucket = p.firstBucket + group.x;
    let bucketStart = select(bucket * 256u - p.phase, 0u, bucket == 0u);
    let start = max(bucketStart, p.offset);
    let end = min((bucket + 1u) * 256u - p.phase, p.offset + p.count);
    let index = start + local.x;
    var s: Summary;
    if (index < end) { s = sample(index, raw[index - p.offset]); }
    lanes[local.x] = s;
    workgroupBarrier();
    for (var stride = 1u; stride < 256u; stride *= 2u) {
        if (local.x % (stride * 2u) == 0u) { lanes[local.x] = merge(lanes[local.x], lanes[local.x + stride]); }
        workgroupBarrier();
    }
    if (local.x == 0u) {
        if (start > bucketStart) { lanes[0] = merge(summaries[bucket - p.pageFirst], lanes[0]); }
        summaries[bucket - p.pageFirst] = lanes[0];
    }
}
`;
  const parentSummaryShader =
    summaryWgsl +
    `
struct Params { childOffset: u32, childCount: u32, parentOffset: u32, phase: u32 };
@group(0) @binding(0) var<storage, read> children: array<Summary>;
@group(0) @binding(1) var<storage, read_write> summaries: array<Summary>;
@group(0) @binding(2) var<uniform> p: Params;
@compute @workgroup_size(64)
fn parent(@builtin(global_invocation_id) invocation: vec3u) {
    let i = invocation.x;
    if (i >= (p.childCount + p.phase + 3u) / 4u) { return; }
    var s: Summary;
    for (var j = 0u; j < 4u; j++) {
        let child = i * 4u + j;
        if (child >= p.phase && child - p.phase < p.childCount) {
            s = merge(s, children[p.childOffset + child - p.phase]);
        }
    }
    summaries[p.parentOffset + i] = s;
}
`;
  const querySummaryShader =
    summaryWgsl +
    `
struct Part { summary: u32, rawOffset: u32, start: u32, count: u32 };
struct Job { first: u32, count: u32 };
struct Params { jobs: u32, origin: u32, slots: u32, pad: u32 };
@group(0) @binding(0) var<storage, read> summaries: array<Summary>;
@group(0) @binding(1) var<storage, read> raw: array<f32>;
@group(0) @binding(2) var<storage, read> parts: array<Part>;
@group(0) @binding(3) var<storage, read> jobs: array<Job>;
@group(0) @binding(4) var<storage, read_write> points: array<vec2f>;
@group(0) @binding(5) var<uniform> p: Params;
@compute @workgroup_size(64)
fn query(@builtin(global_invocation_id) invocation: vec3u) {
    let id = invocation.x;
    if (id >= p.jobs) { return; }
    let job = jobs[id];
    var s: Summary;
    for (var i = 0u; i < job.count; i++) {
        let part = parts[job.first + i];
        if (part.summary != 0xffffffffu) { s = merge(s, summaries[part.summary]); }
        else {
            for (var j = 0u; j < part.count; j++) { s = merge(s, sample(part.start + j, raw[part.rawOffset + j])); }
        }
    }
    let nan = bitcast<f32>(0x7fc00000u | (id & 1u));
    var indices = array<u32, 5>(s.start, s.start + s.count - 1u, s.minIndex, s.maxIndex, s.gapIndex);
    var values = array<f32, 5>(s.first, s.last, s.minimum, s.maximum, nan);
    var count = select(4u, 5u, s.gaps != 0u);
    if (s.finite == 0u || s.gaps >= 2u) { indices[0] = s.gapIndex; values[0] = nan; count = 1u; }
    for (var i = 1u; i < count; i++) {
        let index = indices[i]; let value = values[i]; var j = i;
        while (j > 0u) {
            if (indices[j-1u] <= index) { break; }
            indices[j] = indices[j-1u]; values[j] = values[j-1u]; j--;
        }
        indices[j] = index; values[j] = value;
    }
    var written = 0u; var previous = 0xffffffffu; var point = vec2f(0.0, nan);
    for (var i = 0u; i < count; i++) {
        if (indices[i] == previous) { continue; }
        previous = indices[i];
        point = vec2f(f32(indices[i] - p.origin), select(values[i], nan, invalid(values[i])));
        points[id * p.slots + written] = point; written++;
    }
    while (written < p.slots) { points[id * p.slots + written] = point; written++; }
}
`;

  function pyramidLayout(origin, length) {
    const levels = [];
    let offset = 0;

    for (let stride = 256n; ; stride *= 4n) {
      const first = origin / stride;
      const count = Number((origin + BigInt(length) - 1n) / stride - first + 1n);

      levels.push({ stride, first, count, offset });
      offset += count;

      if (count === 1) {
        return { levels, bytes: offset * summaryBytes };
      }
    }
  }

  function planQuery(origin, length, begin, end, maxPoints, levels) {
    const from = origin + BigInt(begin);
    const to = origin + BigInt(end);
    let stride = 1n;

    while (
      ((to - 1n) / stride - from / stride + 1n) * (stride === 1n ? 1n : 5n) >
      BigInt(maxPoints)
    ) {
      stride *= 4n;
    }

    const parts = [];
    const jobs = [];
    const ranges = [];
    let rawCount = 0;

    for (let start = from; start < to;) {
      const stop = (start / stride + 1n) * stride < to ? (start / stride + 1n) * stride : to;
      const firstPart = parts.length / 4;

      while (start < stop) {
        let chosen;

        for (const level of levels) {
          const bucket = start / level.stride;
          const a = bucket * level.stride > origin ? bucket * level.stride : origin;
          const b =
            (bucket + 1n) * level.stride < origin + BigInt(length)
              ? (bucket + 1n) * level.stride
              : origin + BigInt(length);

          if (a !== start || b > stop) {
            break;
          }

          chosen = { index: level.offset + Number(bucket - level.first), end: b };
        }

        if (chosen) {
          parts.push(chosen.index, 0, 0, 0);
          start = chosen.end;
        } else {
          const end = (start / 256n + 1n) * 256n < stop ? (start / 256n + 1n) * 256n : stop;
          const count = Number(end - start);
          const offset = Number(start - origin);

          parts.push(0xffffffff, rawCount, offset, count);
          const last = ranges.at(-1);

          if (last && last.offset + last.count === offset) {
            last.count += count;
          } else {
            ranges.push({ offset, count });
          }

          rawCount += count;
          start = end;
        }
      }

      jobs.push(firstPart, parts.length / 4 - firstPart);
    }

    return {
      stride,
      parts: new Uint32Array(parts),
      jobs: new Uint32Array(jobs),
      ranges,
      rawCount,
      slots: stride === 1n ? 1 : 5,
    };
  }

  function pipelines(instance) {
    if (!instance.summaryPipelines) {
      instance.summaryPipelines = [baseSummaryShader, parentSummaryShader, querySummaryShader].map(
        (code, i) =>
          instance.device.createComputePipeline({
            layout: "auto",
            compute: {
              module: instance.device.createShaderModule({ code }),
              entryPoint: ["base", "parent", "query"][i],
            },
          }),
      );
    }

    return instance.summaryPipelines;
  }

  function summaryPages(bytes, deviceLimit) {
    // Bound scratch allocations as well as bindings, independently of domain size.
    const pageRecords = Math.floor(Math.min(deviceLimit, 16 * 1024 * 1024) / summaryBytes);

    if (pageRecords < 4) {
      throw new Error(
        `GPU storage buffer limit of ${deviceLimit} bytes cannot fit four summaries.`,
      );
    }

    const pages = [];

    for (let first = 0; first < bytes / summaryBytes; first += pageRecords) {
      pages.push({ first, count: Math.min(pageRecords, bytes / summaryBytes - first) });
    }

    return { pageRecords, pages };
  }

  function copySummaries(encoder, source, first, count, destination, destinationOffset = 0) {
    while (count > 0) {
      const page = source.pages[Math.floor(first / source.pageRecords)];
      const local = first - page.first;
      const copied = Math.min(count, page.count - local);

      encoder.copyBufferToBuffer(
        page.buffer,
        local * summaryBytes,
        destination,
        destinationOffset,
        copied * summaryBytes,
      );

      first += copied;
      count -= copied;
      destinationOffset += copied * summaryBytes;
    }
  }

  function dispatch(instance, pipeline, buffers, count) {
    const encoder = instance.device.createCommandEncoder();
    const pass = encoder.beginComputePass();

    pass.setPipeline(pipeline);

    pass.setBindGroup(
      0,
      instance.device.createBindGroup({
        layout: pipeline.getBindGroupLayout(0),
        entries: buffers.map((buffer, binding) => ({ binding, resource: { buffer } })),
      }),
    );

    pass.dispatchWorkgroups(count);
    pass.end();
    instance.device.queue.submit([encoder.finish()]);
  }

  async function beginPyramid(chartId, id, version, length, origin = 0n, readRange) {
    const instance = await ns.getInstance(chartId);

    if (!Number.isInteger(length) || length < 2 || length > 0xfffffe00) {
      throw new Error(
        "Local GPU summaries support 2 through 4,294,966,784 samples; use server reduction for larger domains.",
      );
    }

    const layout = pyramidLayout(BigInt(origin), length);
    const deviceLimit = Math.min(
      instance.device.limits.maxBufferSize,
      instance.device.limits.maxStorageBufferBindingSize,
    );

    const paging = summaryPages(layout.bytes, deviceLimit);

    const allocated = [];
    const buffer = (size, usage) => {
      const result = ns.createTrackedBuffer(instance, { size, usage });

      allocated.push(result);

      return result;
    };

    try {
      const pages = paging.pages.map((page) => ({
        ...page,
        buffer: buffer(
          page.count * summaryBytes,
          GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST,
        ),
      }));
      const overviewBuffer = pages[0].buffer;
      const transientBuffer = buffer(
        Math.min(ns.streamChunkLength * 4, length * 4, Math.floor(deviceLimit / 4) * 4),
        GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
      );
      const paramsBuffer = buffer(32, GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST);
      const token = ++instance.uploadToken;

      instance.chunkedUploadSessions.set(token, {
        id,
        version,
        length,
        origin: BigInt(origin),
        readRange,
        ...layout,
        pageRecords: paging.pageRecords,
        pages,
        overviewBuffer,
        transientBuffer,
        paramsBuffer,
        writtenLength: 0,
        rangeHasValue: false,
        rangeMinimum: 0,
        rangeMaximum: 0,
      });

      return token;
    } catch (error) {
      allocated.forEach((buffer) => ns.destroyTrackedBuffer(instance, buffer));
      throw error;
    }
  }

  async function processPyramid(chartId, token, offset, count) {
    const instance = await ns.getInstance(chartId);
    const upload = instance.chunkedUploadSessions.get(token);

    if (!upload || upload.writtenLength !== offset || upload.pendingValues?.length !== count) {
      throw ns.cancellationError("Invalid or cancelled summary upload.");
    }

    try {
      for (let position = 0; position < count;) {
        const size = Math.min(count - position, upload.transientBuffer.size / 4);

        instance.device.queue.writeBuffer(
          upload.transientBuffer,
          0,
          upload.pendingValues.subarray(position, position + size),
        );

        await processPyramidSlice(chartId, token, offset + position, size);
        position += size;
      }
    } finally {
      upload.pendingValues = undefined;
    }
  }

  async function processPyramidSlice(chartId, token, offset, count) {
    const instance = await ns.getInstance(chartId);
    const upload = instance.chunkedUploadSessions.get(token);

    if (!upload || upload.writtenLength !== offset || offset + count > upload.length) {
      throw ns.cancellationError("Invalid or cancelled summary upload.");
    }

    const phase = Number(upload.origin % 256n);
    const firstBucket = Math.floor((offset + phase) / 256);
    const groups = Math.floor((offset + count - 1 + phase) / 256) - firstBucket + 1;

    for (let bucket = firstBucket; bucket < firstBucket + groups;) {
      const page = upload.pages[Math.floor(bucket / upload.pageRecords)];
      const batch = Math.min(
        firstBucket + groups - bucket,
        page.first + page.count - bucket,
        65535,
      );

      instance.device.queue.writeBuffer(
        upload.paramsBuffer,
        0,
        new Uint32Array([offset, count, phase, bucket, page.first, 0, 0, 0]),
      );

      dispatch(
        instance,
        pipelines(instance)[0],
        [upload.transientBuffer, page.buffer, upload.paramsBuffer],
        batch,
      );

      bucket += batch;
    }

    const range = await ns.calculateSeriesRangeAsync(instance, upload.transientBuffer, count);

    if (instance.chunkedUploadSessions.get(token) !== upload) {
      throw ns.cancellationError("Summary upload cancelled.");
    }

    if (range.hasValue) {
      upload.rangeMinimum = upload.rangeHasValue
        ? Math.min(upload.rangeMinimum, range.minimum)
        : range.minimum;

      upload.rangeMaximum = upload.rangeHasValue
        ? Math.max(upload.rangeMaximum, range.maximum)
        : range.maximum;

      upload.rangeHasValue = true;
    }

    upload.writtenLength += count;
  }

  async function completePyramid(chartId, token) {
    const instance = await ns.getInstance(chartId);
    const upload = instance.chunkedUploadSessions.get(token);

    if (!upload || upload.writtenLength !== upload.length) {
      throw ns.cancellationError("Incomplete summary upload.");
    }

    const scratch = ns.createTrackedBuffer(instance, {
      size: Math.min(upload.levels[0].count, upload.pageRecords) * summaryBytes,
      usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
    });

    try {
      for (let i = 1; i < upload.levels.length; i++) {
        const child = upload.levels[i - 1];
        const parent = upload.levels[i];

        const phase = Number(child.first % 4n);

        for (let first = 0; first < parent.count;) {
          const page = upload.pages[Math.floor((parent.offset + first) / upload.pageRecords)];
          const batch = Math.min(
            parent.count - first,
            page.first + page.count - parent.offset - first,
            Math.floor(upload.pageRecords / 4),
            65535 * 64,
          );
          const childFirst = Math.max(0, first * 4 - phase);
          const childEnd = Math.min(child.count, (first + batch) * 4 - phase);
          const encoder = instance.device.createCommandEncoder();

          copySummaries(encoder, upload, child.offset + childFirst, childEnd - childFirst, scratch);
          instance.device.queue.submit([encoder.finish()]);

          instance.device.queue.writeBuffer(
            upload.paramsBuffer,
            0,
            new Uint32Array([
              0,
              childEnd - childFirst,
              parent.offset + first - page.first,
              first === 0 ? phase : 0,
            ]),
          );

          dispatch(
            instance,
            pipelines(instance)[1],
            [scratch, page.buffer, upload.paramsBuffer],
            Math.ceil(batch / 64),
          );

          first += batch;
        }
      }
    } finally {
      ns.destroyTrackedBuffer(instance, scratch);
    }

    for (const [key, old] of instance.seriesBuffers) {
      if (old.id === upload.id) {
        ns.destroySeriesBuffer(instance, old);
        instance.seriesBuffers.delete(key);
      }
    }

    instance.seriesBuffers.set(ns.getSeriesKey(upload.id, upload.version, upload.length), {
      ...upload,
      buffer: upload.overviewBuffer,
      pointBuffer: upload.overviewBuffer,
      pyramid: true,
      decimations: new Map(),
    });

    ns.destroyTrackedBuffer(instance, upload.transientBuffer);
    ns.destroyTrackedBuffer(instance, upload.paramsBuffer);
    instance.chunkedUploadSessions.delete(token);

    return {
      hasValue: upload.rangeHasValue,
      minimum: upload.rangeMinimum,
      maximum: upload.rangeMaximum,
    };
  }

  async function getPyramidRenderItem(
    instance,
    source,
    series,
    payload,
    plot,
    target,
    isCurrent = () => true,
  ) {
    const window = ns.getTimeWindow(payload, series, source.length);

    if (!window) {
      return null;
    }

    const begin = ns.valueOf(series, "ViewFirst") ?? Math.max(0, window.first - 1);
    const end =
      ns.valueOf(series, "ViewEnd") ??
      Math.min(source.length, Math.ceil(window.indexLeft + window.indexRange) + 1);
    const budget =
      ns.valueOf(series, "PointBudget") ??
      Math.min(32768, Math.max(16, Math.ceil(plot.plotWidth * 4)));
    const key = `${begin}:${end}:${budget}`;
    let view = source.decimations.get(target);

    if (view?.key !== key) {
      const plan = planQuery(source.origin, source.length, begin, end, budget, source.levels);
      const raw = new Float32Array(plan.rawCount);
      let offset = 0;

      for (const range of plan.ranges) {
        const values = await source.readRange(range.offset, range.count);

        // A released/replaced target must not regain buffers after an asynchronous read.
        if (!isCurrent()) {
          return null;
        }

        if (values.length !== range.count) {
          throw new Error("Incomplete raw summary boundary.");
        }

        raw.set(values, offset);
        offset += values.length;
      }

      if (instance.disposed || ![...instance.seriesBuffers.values()].includes(source)) {
        throw ns.cancellationError("Summary view superseded.");
      }

      const allocated = [];
      const buffer = (data, usage) => {
        const size = Math.max(8, typeof data === "number" ? data : data.byteLength);
        const limit = Math.min(
          instance.device.limits.maxBufferSize,
          instance.device.limits.maxStorageBufferBindingSize,
        );

        if (size > limit) {
          throw new Error(
            `Bounded viewport query requires ${size} bytes, exceeding the GPU binding limit of ${limit} bytes.`,
          );
        }

        const result = ns.createTrackedBuffer(instance, {
          size,
          usage,
        });

        allocated.push(result);

        if (typeof data !== "number" && data.byteLength) {
          instance.device.queue.writeBuffer(result, 0, data);
        }

        return result;
      };

      try {
        const count = plan.jobs.length / 2;
        const outputBuffer = buffer(
          count * plan.slots * 8,
          GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC,
        );
        const usage = GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST;
        // Gather selected records on the GPU. Jobs retain global bucket ordering,
        // so merging across page boundaries has exactly the unsharded semantics.
        let selected = 0;

        for (let i = 0; i < plan.parts.length; i += 4) {
          if (plan.parts[i] !== 0xffffffff) {
            selected++;
          }
        }

        const summaryBuffer = buffer(Math.max(summaryBytes, selected * summaryBytes), usage);
        const encoder = instance.device.createCommandEncoder();
        let destination = 0;

        for (let i = 0; i < plan.parts.length; i += 4) {
          if (plan.parts[i] !== 0xffffffff) {
            copySummaries(
              encoder,
              source,
              plan.parts[i],
              1,
              summaryBuffer,
              destination * summaryBytes,
            );

            plan.parts[i] = destination++;
          }
        }

        instance.device.queue.submit([encoder.finish()]);
        const rawBuffer = buffer(raw, usage);
        const partsBuffer = buffer(plan.parts, usage);
        const jobsBuffer = buffer(plan.jobs, usage);
        const paramsBuffer = buffer(
          new Uint32Array([count, begin, plan.slots, 0]),
          GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        );

        dispatch(
          instance,
          pipelines(instance)[2],
          [summaryBuffer, rawBuffer, partsBuffer, jobsBuffer, outputBuffer, paramsBuffer],
          Math.ceil(count / 64),
        );

        ns.destroyTrackedBuffer(instance, view?.outputBuffer);

        view = {
          key,
          outputBuffer,
          length: count * plan.slots,
          renderBuffer: { buffer: outputBuffer, pointBuffer: outputBuffer, dataMode: 1 },
        };

        source.decimations.set(target, view);

        for (const buffer of allocated.slice(1)) {
          ns.destroyTrackedBuffer(instance, buffer);
        }
      } catch (error) {
        allocated.forEach((buffer) => ns.destroyTrackedBuffer(instance, buffer));
        throw error;
      }
    }

    return {
      seriesBuffer: view.renderBuffer,
      zoomInfo: {
        first: 0,
        segmentCount: Math.max(0, view.length - 1),
        zoomedLeft:
          plot.plotLeft + ((begin - window.indexLeft) / window.indexRange) * plot.plotWidth,
        dx: plot.plotWidth / window.indexRange,
      },
    };
  }

  Object.assign(ns, {
    summaryWgsl,
    baseSummaryShader,
    parentSummaryShader,
    querySummaryShader,
    pyramidLayout,
    summaryPages,
    planQuery,
    getPyramidRenderItem,
    beginChunkedSeriesAsync: beginPyramid,
    processChunkedSeriesUploadAsync: processPyramid,
    completeChunkedSeriesAsync: completePyramid,
  });
})();
