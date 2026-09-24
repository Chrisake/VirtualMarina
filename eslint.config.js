// Lint for the JavaScript the libraries and apps ship to the browser. Run with `npm ci && npm run lint`;
// CI runs it too (.github/workflows/ci.yml, job "javascript").
import js from "@eslint/js";
import globals from "globals";

const shared = {
    ...js.configs.recommended.rules,
    "no-unused-vars": ["error", { args: "after-used", argsIgnorePattern: "^_", caughtErrors: "none" }],
    eqeqeq: ["error", "always"],
};

export default [
    {
        // Build output and restored packages carry copies of these files and third-party scripts.
        ignores: ["**/bin/**", "**/obj/**", "**/node_modules/**", "**/_framework/**"],
    },
    {
        // The WebGL renderer: an ES module imported by MarinaView through IJSRuntime.
        files: ["src/VirtualMarina.Blazor/wwwroot/**/*.js"],
        languageOptions: {
            ecmaVersion: 2022,
            sourceType: "module",
            globals: { ...globals.browser },
        },
        rules: shared,
    },
    {
        // The Blazor designer's helpers: a classic script included by index.html, publishing window.vmDesigner.
        files: ["apps/**/wwwroot/**/*.js", "samples/**/wwwroot/**/*.js"],
        languageOptions: {
            ecmaVersion: 2022,
            sourceType: "script",
            globals: { ...globals.browser },
        },
        rules: shared,
    },
];
