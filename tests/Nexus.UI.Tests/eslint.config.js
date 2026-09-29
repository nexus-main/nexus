// ESLint config for Nexus UI integration tests in tests/Nexus.UI.Tests/app/.
// This is a separate config because ESLint v10 flat config cannot lint files
// outside the config file's base path — the main config at src/Nexus.UI/
// eslint.config.js can only cover files under src/Nexus.UI/.
//
// Dependencies (typescript-eslint, eslint-config-prettier) are not installed
// in this directory, so createRequire is used to load them from
// src/Nexus.UI/node_modules/. Shared rule definitions are imported from
// src/Nexus.UI/eslint-rules.cjs to avoid duplication.
const { createRequire } = require("module");
const path = require("path");

const uiNodeModules = path.resolve(__dirname, "../../src/Nexus.UI/node_modules");
const requireFromUI = createRequire(uiNodeModules + "/");

const tseslint = requireFromUI("typescript-eslint");
const eslintConfigPrettier = requireFromUI("eslint-config-prettier");
const { paddingLineBetweenStatements, noUnusedVars } = require("../../src/Nexus.UI/eslint-rules.cjs");

module.exports = tseslint.config(
  {
    files: ["app/**/*.{ts,js}"],
    languageOptions: {
      parser: tseslint.parser,
      parserOptions: {
        ecmaVersion: 2022,
        sourceType: "module",
      },
    },
    plugins: { "@typescript-eslint": tseslint.plugin },
    rules: {
      "padding-line-between-statements": paddingLineBetweenStatements,
      "@typescript-eslint/no-unused-vars": noUnusedVars,
    },
  },
  eslintConfigPrettier,
  {
    files: ["app/**/*.{ts,js}"],
    rules: {
      curly: ["error", "all"],
    },
  },
);
