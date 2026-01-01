# Monaco Editor Setup

**Install:**
npm install monaco-editor

**angular.json:**
{ "assets": [ { "glob": "**/*", "input": "node_modules/monaco-editor", "output": "/assets/monaco/" } ], "styles": [ "node_modules/monaco-editor/min/vs/editor/editor.main.css" ] }

**main.ts:**
(window as any).MonacoEnvironment = { getWorkerUrl: (moduleId: string, label: string) => { if (label === 'python') return './assets/monaco/esm/vs/basic-languages/python/python.js'; if (label === 'javascript') return './assets/monaco/esm/vs/language/typescript/ts.worker.js'; if (label === 'csharp') return './assets/monaco/esm/vs/basic-languages/csharp/csharp.js'; if (label === 'java') return './assets/monaco/esm/vs/basic-languages/java/java.js'; return './assets/monaco/esm/vs/editor/editor.worker.js'; }, };

**Component:**
import * as monaco from 'monaco-editor';
@Component({ selector: 'app-code-editor', template: <div #editorContainer class="h-[600px]"></div>, changeDetection: ChangeDetectionStrategy.OnPush }) export class CodeEditorComponent { code = input.required<string>(); language = input.required<'python' | 'javascript' | 'csharp' | 'java'>(); codeChange = output<string>();
private editorContainer = viewChild<ElementRef>('editorContainer'); private editor?: monaco.editor.IStandaloneCodeEditor;
constructor() { effect(() => { if (this.editorContainer()) { this.initEditor(); } }); }
private initEditor() { const container = this.editorContainer()!.nativeElement; this.editor = monaco.editor.create(container, { value: this.code(), language: this.language(), theme: 'vs-dark', automaticLayout: true, minimap: { enabled: false }, fontSize: 14, });
this.editor.onDidChangeModelContent(() => {
  this.codeChange.emit(this.editor!.getValue());
});
}
ngOnDestroy() { this.editor?.dispose(); } }