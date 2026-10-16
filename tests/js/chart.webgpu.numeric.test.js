const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");
const {
  referenceProjection,
  thresholdFixtures,
  signedZeroFixtures,
} = require("./chart.projection.fixtures.js");

test(
  "GPU reduction matches canonical projection fixtures",
  { skip: !process.env.NEXUS_WEBGPU_MODULE },
  async () => {
    const { create, globals } = await import(process.env.NEXUS_WEBGPU_MODULE);

    Object.assign(globalThis, globals);
    const gpu = create([]);
    const adapter = await gpu.requestAdapter();

    assert.ok(adapter, "A GPU adapter is required for numerical verification");
    const device = await adapter.requestDevice({
      requiredLimits: { maxStorageBuffersPerShaderStage: 5 },
    });
    const context = { window: {}, ...globals, Float32Array, Uint32Array };

    vm.runInNewContext(
      fs.readFileSync(
        path.join(__dirname, "../../src/Nexus.UI/src/charts/chart.webgpu.shaders.js"),
        "utf8",
      ),
      context,
    );

    vm.runInNewContext(
      fs.readFileSync(
        path.join(__dirname, "../../src/Nexus.UI/src/charts/chart.webgpu.pyramid.js"),
        "utf8",
      ),
      context,
    );

    const shaders = context.window.__nexusChartWebGpu;

    try {
      for (const name of [
        "shader",
        "decimationShader",
        "pointDecimationShader",
        "rangeShader",
        "baseSummaryShader",
        "parentSummaryShader",
        "querySummaryShader",
      ]) {
        const module = device.createShaderModule({ code: shaders[name] });
        const info = await module.getCompilationInfo();

        assert.deepEqual(
          info.messages
            .filter((message) => message.type === "error")
            .map((message) => message.message),
          [],
          name,
        );
      }

      for (const [name, entryPoint] of [
        ["baseSummaryShader", "base"],
        ["parentSummaryShader", "parent"],
        ["querySummaryShader", "query"],
      ]) {
        await device.createComputePipelineAsync({
          layout: "auto",
          compute: { module: device.createShaderModule({ code: shaders[name] }), entryPoint },
        });
      }

      // Prebuilt records exercise u32-scale merge/projection without allocating billions of samples.
      for (const count of [0xfffffffe, 0xffffffff]) {
        for (const invalidCount of [
          Math.ceil(count / 2) - 1,
          Math.ceil(count / 2),
          Math.ceil(count / 2) + 1,
        ]) {
          const records = new ArrayBuffer(96);
          const words = new Uint32Array(records);
          const floats = new Float32Array(records);
          const firstCount = Math.floor(count / 2);

          for (let i = 0; i < 2; i++) {
            const offset = i * 12;
            const start = i === 0 ? 0 : firstCount;

            words.set([start, i === 0 ? firstCount : count - firstCount], offset);
            floats.set([3, 2, -8, 10], offset + 2);

            words.set(
              [
                start + 2,
                start + 3,
                start + 1,
                i === 0
                  ? Math.floor(invalidCount / 2)
                  : invalidCount - Math.floor(invalidCount / 2),
                1,
                0,
              ],
              offset + 6,
            );
          }

          const inputs = [
            words,
            new Uint32Array(2),
            new Uint32Array([0, 0, 0, 0, 1, 0, 0, 0]),
            new Uint32Array([0, 2]),
          ];
          const buffers = inputs.map((data) => {
            const buffer = device.createBuffer({
              size: data.byteLength,
              usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
            });

            device.queue.writeBuffer(buffer, 0, data);

            return buffer;
          });
          const output = device.createBuffer({
            size: 40,
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC,
          });
          const params = device.createBuffer({
            size: 16,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
          });

          device.queue.writeBuffer(params, 0, new Uint32Array([1, 0, 5, 0]));
          buffers.push(output, params);
          const pipeline = await device.createComputePipelineAsync({
            layout: "auto",
            compute: {
              module: device.createShaderModule({ code: shaders.querySummaryShader }),
              entryPoint: "query",
            },
          });
          const readback = device.createBuffer({
            size: 40,
            usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ,
          });
          const encoder = device.createCommandEncoder();
          const pass = encoder.beginComputePass();

          pass.setPipeline(pipeline);

          pass.setBindGroup(
            0,
            device.createBindGroup({
              layout: pipeline.getBindGroupLayout(0),
              entries: buffers.map((buffer, binding) => ({ binding, resource: { buffer } })),
            }),
          );

          pass.dispatchWorkgroups(1);
          pass.end();
          encoder.copyBufferToBuffer(output, 0, readback, 0, 40);
          device.queue.submit([encoder.finish()]);
          await readback.mapAsync(GPUMapMode.READ);
          const expected =
            invalidCount >= Math.ceil(count / 2)
              ? Array.from({ length: 5 }, () => [1, NaN]).flat()
              : [0, 3, 1, NaN, 2, -8, 3, 10, Math.fround(count - 1), 2];

          assert.deepEqual(
            [...new Float32Array(readback.getMappedRange())],
            expected,
            `u32 count=${count} invalidCount=${invalidCount}`,
          );

          readback.unmap();

          for (const buffer of [...buffers, readback]) {
            buffer.destroy();
          }
        }
      }

      // Exercise the production upload, parent-build and query functions, not a
      // second shader implementation. Read back only the bounded drawing output.
      const instance = {
        device,
        chunkedUploadSessions: new Map(),
        seriesBuffers: new Map(),
        uploadToken: 0,
        disposed: false,
      };

      Object.assign(shaders, {
        getInstance: async () => instance,
        createTrackedBuffer: (_instance, descriptor) => {
          assert.ok(
            descriptor.size <= instance.device.limits.maxStorageBufferBindingSize,
            `oversized allocation ${descriptor.size}`,
          );

          return device.createBuffer(descriptor);
        },
        destroyTrackedBuffer: (_instance, buffer) => buffer?.destroy(),
        calculateSeriesRangeAsync: async () => ({ hasValue: false }),
        getSeriesKey: (id, version, length) => `${id}:${version}:${length}`,
        valueOf: (object, name) => object[name[0].toLowerCase() + name.slice(1)],
        cancellationError: (message) => new Error(message),
        getTimeWindow: (_payload, series) => ({
          first: series.viewFirst,
          last: series.viewEnd - 1,
          indexLeft: series.viewFirst,
          indexRange: series.viewEnd - series.viewFirst,
        }),
      });

      const pyramidFixtures = [
        {
          name: "sparse gaps",
          values: Float32Array.from({ length: 4103 }, (_, i) =>
            i % 517 < 3 ? NaN : (i % 41) - 20,
          ),
        },
        { name: "constant", values: new Float32Array(4103).fill(7) },
        {
          name: "alternating zero",
          values: Float32Array.from({ length: 4103 }, (_, i) => (i % 2 ? 0 : -0)),
        },
        {
          name: "mixed nonfinite",
          values: Float32Array.from({ length: 4103 }, (_, i) =>
            i % 7 === 0 ? Infinity : i % 7 === 3 ? -Infinity : i % 7 === 5 ? NaN : i,
          ),
        },
        { name: "all infinity", values: new Float32Array(4103).fill(Infinity) },
        ...[
          [4104, 2051],
          [4104, 2052],
          [4104, 2053],
          [4103, 2051],
          [4103, 2052],
        ].map(([count, invalidCount]) => ({
          name: `density count=${count} invalid=${invalidCount}`,
          values: Float32Array.from({ length: count }, (_, i) =>
            i > 0 && i <= invalidCount ? [NaN, Infinity, -Infinity][i % 3] : (i % 41) - 20,
          ),
        })),
        // At origin zero, child [256, 512) collapses by itself, but its
        // interior extrema must survive parent construction and query merges.
        {
          name: "hidden extrema in mostly-invalid child",
          values: Float32Array.from({ length: 4103 }, (_, i) =>
            i === 273 ? -1000 : i === 449 ? 1000 : i >= 256 && i < 512 ? NaN : 2,
          ),
        },
        ...signedZeroFixtures.map(({ name, values }) => ({
          name,
          values: Float32Array.from({ length: 4103 }, (_, i) =>
            i === 129 ? values[1] : i === 385 ? values[2] : values[0],
          ),
        })),
      ];

      for (const cap of [device.limits.maxStorageBufferBindingSize, 4096]) {
        instance.device = new Proxy(device, {
          get(target, key) {
            if (key === "limits") {
              return { maxBufferSize: cap, maxStorageBufferBindingSize: cap };
            }

            const value = Reflect.get(target, key, target);

            return typeof value === "function" ? value.bind(target) : value;
          },
        });

        for (const origin of [0n, 123n, 255n, 1023n, 9007199254740993123n]) {
          for (const { name, values: raw } of pyramidFixtures) {
            const input =
              cap === 4096
                ? Float32Array.from({ length: 65543 }, (_, i) => raw[i % raw.length])
                : raw;
            const token = await shaders.beginChunkedSeriesAsync(
              "chart",
              "s",
              0,
              input.length,
              origin,
              async (offset, count) => input.slice(offset, offset + count),
            );
            const upload = instance.chunkedUploadSessions.get(token);

            if (cap === 4096) {
              assert.ok(upload.pages.length > 1);
            }

            // Deliberately split a base bucket over upload calls.
            for (const [start, end] of [
              [0, 731],
              [731, 4096],
              [4096, input.length],
            ]) {
              upload.pendingValues = input.subarray(start, end);
              await shaders.processChunkedSeriesUploadAsync("chart", token, start, end - start);
            }

            await shaders.completeChunkedSeriesAsync("chart", token);
            const source = instance.seriesBuffers.get(`s:0:${input.length}`);
            const baseBoundary = 256 - Number(origin % 256n);
            const views = [
              ["full single bucket", 0, input.length, 5],
              ["full multibucket", 0, input.length, 40],
              ["interior", 17, input.length - 102, 20],
              ["raw", 700, 725, 32],
              ["partial bases", 1000, 1801, 80],
              ["exact base", baseBoundary, baseBoundary + 256, 5],
              ["exact four bases", baseBoundary, baseBoundary + 1024, 5],
            ];

            if (cap === 4096) {
              const pageBoundary = source.pages[1].first * 256 - Number(origin % 256n);

              views.push(
                ["paged interior", 21001, 43019, 40],
                ["ends at page", pageBoundary - 512, pageBoundary, 10],
                ["starts at page", pageBoundary, pageBoundary + 512, 10],
                ["straddles page", pageBoundary - 256, pageBoundary + 256, 5],
              );
            }

            for (const [view, begin, end, budget] of views) {
              const series = { viewFirst: begin, viewEnd: end, pointBudget: budget };
              const item = await shaders.getPyramidRenderItem(
                instance,
                source,
                series,
                {},
                { plotLeft: 0, plotWidth: 100 },
                "main",
              );
              const output = item.seriesBuffer.pointBuffer;
              const readback = device.createBuffer({
                size: output.size,
                usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ,
              });
              const encoder = device.createCommandEncoder();

              encoder.copyBufferToBuffer(output, 0, readback, 0, output.size);
              device.queue.submit([encoder.finish()]);
              await readback.mapAsync(GPUMapMode.READ);
              const actual = [...new Float32Array(readback.getMappedRange())];
              const plan = shaders.planQuery(
                origin,
                input.length,
                begin,
                end,
                budget,
                source.levels,
              );
              const expected = [];

              for (let start = begin; start < end;) {
                const stop = Math.min(
                  end,
                  Number(((origin + BigInt(start)) / plan.stride + 1n) * plan.stride - origin),
                );
                const points = referenceProjection(input.subarray(start, stop), start);

                while (points.length < plan.slots) {
                  points.push(points.at(-1));
                }

                expected.push(...points.flatMap(([index, value]) => [index - begin, value]));
                start = stop;
              }

              assert.deepEqual(
                actual,
                expected,
                `pyramid ${name} cap=${cap} origin=${origin} ${view}=${begin}:${end} budget=${budget}`,
              );

              readback.unmap();
              readback.destroy();
            }

            for (const view of source.decimations.values()) {
              view.outputBuffer.destroy();
            }

            source.pages.forEach((page) => page.buffer.destroy());
            instance.seriesBuffers.clear();
          }
        }
      }

      const fixtures = [
        { name: "finite extrema", values: [3, 1, 8, 4] },
        { name: "interior gap", values: [5, NaN, 1, 8, 4] },
        { name: "invalid endpoints exact half", values: [NaN, 1, 8, NaN] },
        { name: "singleton", values: [1] },
        { name: "alternating zero", values: [-0, 0, -0, 0] },
        {
          name: "cross-lane extrema ties",
          values: Array.from({ length: 256 }, (_, i) =>
            i === 1 || i === 128 ? -5 : i === 3 || i === 192 ? 8 : 0,
          ),
        },
        {
          name: "leading majority gaps",
          values: Array.from({ length: 129 }, (_, i) => (i < 65 ? NaN : i)),
        },
        ...thresholdFixtures,
        ...signedZeroFixtures,
        {
          name: "uneven multibucket offsets",
          first: 7,
          visibleLength: 773,
          bucketCount: 3,
          globalOffset: 1027,
          outputBucket: 2,
          values: Array.from({ length: 797 }, (_, i) =>
            i < 7 || i >= 780 ? 10000 : i % 19 === 3 ? NaN : (i % 47) - 23,
          ),
        },
      ];

      for (const {
        name: fixture,
        values: input,
        first = 0,
        visibleLength = input.length,
        bucketCount = 1,
      } of fixtures) {
        const values = Float32Array.from(input);

        for (const [name, entryPoint, points] of [
          ["decimationShader", "decimate", false],
          ["pointDecimationShader", "decimatePoints", true],
        ]) {
          const buckets = bucketCount;
          const outputSize = buckets * 40;
          const xs = Float32Array.from(input, (_, i) => 17.5 + (i * i) / 8 + (i % 3) / 4);
          const data = points
            ? Float32Array.from(input.flatMap((value, i) => [xs[i], value]))
            : values;
          const source = device.createBuffer({
            size: data.byteLength,
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
          });
          const output = device.createBuffer({
            size: outputSize,
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST,
          });
          const params = device.createBuffer({
            size: 16,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
          });
          const readback = device.createBuffer({
            size: outputSize,
            usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ,
          });

          device.queue.writeBuffer(source, 0, data);
          device.queue.writeBuffer(output, 0, new Float32Array(outputSize / 4).fill(-12345));

          device.queue.writeBuffer(
            params,
            0,
            new Uint32Array([first, visibleLength, buckets, values.length]),
          );

          const pipeline = await device.createComputePipelineAsync({
            layout: "auto",
            compute: { module: device.createShaderModule({ code: shaders[name] }), entryPoint },
          });
          const bindGroup = device.createBindGroup({
            layout: pipeline.getBindGroupLayout(0),
            entries: [source, output, params].map((buffer, binding) => ({
              binding,
              resource: { buffer },
            })),
          });
          const encoder = device.createCommandEncoder();
          const pass = encoder.beginComputePass();

          pass.setPipeline(pipeline);
          pass.setBindGroup(0, bindGroup);
          pass.dispatchWorkgroups(buckets);
          pass.end();
          encoder.copyBufferToBuffer(output, 0, readback, 0, outputSize);
          device.queue.submit([encoder.finish()]);
          await readback.mapAsync(GPUMapMode.READ);
          const actual = [...new Float32Array(readback.getMappedRange())];
          const expected = [];

          for (let bucket = 0; bucket < buckets; bucket++) {
            const width = Math.floor(visibleLength / buckets);
            const remainder = visibleLength % buckets;
            const start = first + bucket * width + Math.min(bucket, remainder);
            const end = Math.min(
              values.length,
              first + (bucket + 1) * width + Math.min(bucket + 1, remainder),
            );
            const projected = referenceProjection(values.subarray(start, end), start);

            while (projected.length < 5) {
              projected.push(projected.at(-1));
            }

            expected.push(
              ...projected.flatMap(([index, value]) => [points ? xs[index] : index - first, value]),
            );
          }

          assert.deepEqual(actual, expected, `${name}: ${fixture}`);
          readback.unmap();

          for (const buffer of [source, output, params, readback]) {
            buffer.destroy();
          }
        }
      }
    } finally {
      device.destroy();
    }
  },
);
