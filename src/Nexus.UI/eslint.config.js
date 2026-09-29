import tseslint from 'typescript-eslint'

// The only enforced rule is the auto-fixable core padding-line-between-statements rule,
// which inserts/removes blank lines between statements. It is the standard tool for
// "enforce spacing between lines of code". The @typescript-eslint/ variant of this rule
// was removed from the typescript-eslint plugin, so the core rule is used instead; it
// recognizes the statement types relevant here (const/let/var, if/for/while/switch/try,
// return, multiline-expression) on both TS and JS via the @typescript-eslint parser.
// Other readability rules (max-len, max-statements-per-line, brace-style) are intentionally
// NOT enabled: they are not auto-fixable and would require manual rewrites.
// nonblock-statement-body-position ("below") forbids single-line control bodies such as
// `if (x) y;` by moving the body onto its own line; it is whitespace-fixable (semantically inert).
const paddingLineBetweenStatements = [
  'error',
  // blank line after a run of variable declarations
  { blankLine: 'always', prev: ['const', 'let', 'var'], next: '*' },
  { blankLine: 'any', prev: ['const', 'let', 'var'], next: ['const', 'let', 'var'] },
  // blank line before/after control flow
  { blankLine: 'always', prev: '*', next: ['if', 'for', 'while', 'switch', 'try', 'return'] },
  { blankLine: 'always', prev: ['if', 'for', 'while', 'switch', 'try'], next: '*' },
  // blank line before/after multiline expressions
  { blankLine: 'always', prev: '*', next: 'multiline-expression' },
  { blankLine: 'always', prev: 'multiline-expression', next: '*' },
]

export default tseslint.config(
  {
    ignores: ['dist/**', 'node_modules/**', 'out-tsc/**', '.angular/**'],
  },
  {
    files: ['src/**/*.{ts,js}'],
    languageOptions: {
      parser: tseslint.parser,
      parserOptions: {
        ecmaVersion: 2022,
        sourceType: 'module',
      },
    },
    rules: {
      'padding-line-between-statements': paddingLineBetweenStatements,
      // forbid single-line control bodies: `if (x) y;` -> `if (x)\n  y;`
      'nonblock-statement-body-position': ['error', 'below'],
    },
  },
)
