import tseslint from "typescript-eslint";
import eslintConfigPrettier from "eslint-config-prettier";

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

export default tseslint.config(
  {
    ignores: ["dist/**", "node_modules/**", "out-tsc/**", ".angular/**"],
  },
  {
    files: ["src/**/*.{ts,js}"],
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
      "@typescript-eslint/no-unused-vars": [
        "error",
        {
          argsIgnorePattern: "^_",
          varsIgnorePattern: "^_",
          caughtErrorsIgnorePattern: "^_",
        },
      ],
    },
  },
  eslintConfigPrettier,
  // Re-enable curly after eslint-config-prettier, which disables it as a
  // precaution. Prettier preserves brace style (never adds or removes braces),
  // so there is no actual conflict — the two tools are complementary.
  {
    files: ["src/**/*.{ts,js}"],
    rules: {
      curly: ["error", "all"],
    },
  },
);
