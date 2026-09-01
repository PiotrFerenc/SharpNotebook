// editor.main.js is loaded as a plain <script> tag but still defines itself as an AMD module — the
// global `monaco` object doesn't exist until something actually calls require(['vs/editor/editor.main']).
// BlazorMonaco triggers that itself when it constructs an editor, but nothing does before the first one
// exists — and Home registers its page-wide completion provider in OnAfterRenderAsync(firstRender), which
// fires before any cell (and so any editor) exists. Force the load explicitly first.
window.sharpNotebookEnsureMonacoLoaded = () => new Promise((resolve) => {
    if (window.monaco) {
        resolve();
        return;
    }
    require(['vs/editor/editor.main'], () => resolve());
});
