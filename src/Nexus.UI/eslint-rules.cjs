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

module.exports = { paddingLineBetweenStatements, noUnusedVars };
