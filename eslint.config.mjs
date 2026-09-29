import { createRequire } from "module";

// ESLint flat config at the repo root. A single config covers both the Angular
// app source (src/Nexus.UI/src/**) and the UI integration tests
// (tests/Nexus.UI.Tests/app/**). ESLint v10 uses CWD as base path, so this
// config must be invoked from the repo root — the npm scripts in
// src/Nexus.UI/package.json `cd ../..` before running ESLint.
//
// Dependencies (typescript-eslint, eslint-config-prettier) live in
// src/Nexus.UI/node_modules/, so createRequire resolves them from there.
const require = createRequire(new URL("./src/Nexus.UI/node_modules/", import.meta.url));

const tseslint = require("typescript-eslint");
const eslintConfigPrettier = require("eslint-config-prettier");

// Prettier owns all formatting (indentation, braces, quotes, semicolons, line
// wrapping, trailing commas). eslint-config-prettier is appended last to turn
// off every ESLint formatting rule that would otherwise conflict with Prettier.
// The only ESLint rules kept here are non-formatting readability rules that
// Prettier does not handle:
//   - padding-line-between-statements: enforce blank lines between statements
//     (Prettier preserves blank lines but does not add them). Uses the core
//     rule because the @typescript-eslint/ variant was removed in v8; it
//     recognizes the relevant statement types (const/let/var, if/for/while/
//     switch/try, return, multiline-expression) on both TS and JS.
//   - curly ("all"): require braces on every if/else/for/while. Prettier then
//     expands the block across multiple lines with correct indentation, which
//     is what forbids the single-line `if (x) return;` form.
//   - @typescript-eslint/no-unused-vars: error on unused locals, imports, and
//     parameters. Variables/args/caught-errors prefixed with `_` are exempt.
const paddingLineBetweenStatements = [
  "error",
  // blank line after a run of variable declarations
  { blankLine: "always", prev: ["const", "let", "var"], next: "*" },
  { blankLine: "any", prev: ["const", "let", "var"], next: ["const", "let", "var"] },
  // blank line before/after control flow
  { blankLine: "always", prev: "*", next: ["if", "for", "while", "switch", "try", "return"] },
  { blankLine: "always", prev: ["if", "for", "while", "switch", "try"], next: "*" },
  // blank line before/after multiline expressions
  { blankLine: "always", prev: "*", next: "multiline-expression" },
  { blankLine: "always", prev: "multiline-expression", next: "*" },
];

const noUnusedVars = [
  "error",
  {
    argsIgnorePattern: "^_",
    varsIgnorePattern: "^_",
    caughtErrorsIgnorePattern: "^_",
  },
];

const uiFiles = ["src/Nexus.UI/src/**/*.{ts,js}", "tests/Nexus.UI.Tests/app/**/*.{ts,js}"];

export default tseslint.config(
  {
    ignores: ["**/dist/**", "**/node_modules/**", "**/out-tsc/**", "**/.angular/**"],
  },
  {
    files: uiFiles,
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
  // Re-enable curly after eslint-config-prettier, which disables it as a
  // precaution. Prettier preserves brace style (never adds or removes braces),
  // so there is no actual conflict — the two tools are complementary.
  {
    files: uiFiles,
    rules: {
      curly: ["error", "all"],
    },
  },
);
