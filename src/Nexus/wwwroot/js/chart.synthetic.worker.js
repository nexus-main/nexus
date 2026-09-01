const streams = new Map();
const cancelled = new Set();

function generate(offset, count, kind) {
    const values = new Float32Array(count);

    for (let localIndex = 0; localIndex < count; localIndex++) {
        const index = offset + localIndex;

        if (kind === 'WindSpeed') {
            values[localIndex] = index / 4;
        } else {
            let hash = (index + 1) >>> 0;
            hash = Math.imul(hash ^ (hash >>> 16), 0x7feb352d) >>> 0;
            hash = Math.imul(hash ^ (hash >>> 15), 0x846ca68b) >>> 0;
            const random = ((hash ^ (hash >>> 16)) >>> 0) / 4294967296;
            values[localIndex] = kind === 'Temperature'
                ? random * 10 - 5
                : random * 100 + 1000;
        }
    }

    if (kind === 'WindSpeed') {
        for (const index of [0, 5, 6, 10, 11, 12, 15, 16, 17, 18]) {
            if (offset <= index && index < offset + count)
                values[index - offset] = NaN;
        }
    }

    return values;
}

function sendNext(requestId) {
    const stream = streams.get(requestId);

    if (!stream || cancelled.delete(requestId)) {
        streams.delete(requestId);
        return;
    }

    if (stream.offset >= stream.length) {
        streams.delete(requestId);
        self.postMessage({ requestId, complete: true });
        return;
    }

    const count = Math.min(stream.chunkLength, stream.length - stream.offset);
    const offset = stream.offset;
    const values = generate(offset, count, stream.kind);
    stream.offset += count;
    self.postMessage({ requestId, offset, values }, [values.buffer]);
}

self.onmessage = event => {
    const message = event.data;

    if (message.type === 'stream') {
        streams.set(message.requestId, {
            length: message.length,
            kind: message.kind,
            chunkLength: message.chunkLength,
            offset: 0,
        });
        sendNext(message.requestId);
    } else if (message.type === 'ack') {
        sendNext(message.requestId);
    } else if (message.type === 'raw') {
        if (cancelled.delete(message.requestId))
            return;

        const values = generate(message.offset, message.count, message.kind);
        self.postMessage({ requestId: message.requestId, offset: message.offset, values, complete: true }, [values.buffer]);
    } else if (message.type === 'cancel') {
        cancelled.add(message.requestId);
        streams.delete(message.requestId);
    }
};
