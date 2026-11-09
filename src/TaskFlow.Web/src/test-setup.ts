import '@testing-library/jest-dom/vitest'

// jsdom does not calculate layout. Drag sensors are exercised in the browser,
// while component tests only need the observer lifecycle to be available.
if (!globalThis.ResizeObserver) {
  globalThis.ResizeObserver = class implements ResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
}
