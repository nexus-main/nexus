import { defineConfig } from "vitest/config";
import { fileURLToPath } from "url";

const nexusApiPath = fileURLToPath(new URL("../../../src/clients/typescript/index.ts", import.meta.url));

export default defineConfig({
    test: {
        include: ["*-tests.ts"],
        globals: true,
    },
    resolve: {
        alias: {
            "nexus-api": nexusApiPath,
        },
    },
});
