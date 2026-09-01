self.onmessage = event => {
    const { length, kind, chunkLength } = event.data;
    for (let offset = 0; offset < length; offset += chunkLength) {
        const count = Math.min(chunkLength, length - offset);
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

        self.postMessage({ offset, values }, [values.buffer]);
    }

    self.postMessage({ complete: true });
};
