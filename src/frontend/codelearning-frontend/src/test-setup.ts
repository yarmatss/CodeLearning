// jsdom lacks some browser APIs that monaco-editor touches as soon as it is imported
// (e.g. its clipboard support check), so spec files that load the code editor would fail to load.

if (typeof document.queryCommandSupported !== 'function') {
  document.queryCommandSupported = () => false;
}

if (typeof window.matchMedia !== 'function') {
  window.matchMedia = (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false
  });
}
