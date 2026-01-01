import { Component, ElementRef, viewChild, effect, input, output, ChangeDetectionStrategy } from '@angular/core';
import * as monaco from 'monaco-editor';

@Component({
  selector: 'app-code-editor',
  template: `
    <div 
      #editorContainer 
      class="border border-gray-300 rounded-lg"
      [style.height]="height()"
    ></div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CodeEditorComponent {
  // Inputs
  code = input.required<string>();
  language = input<string>('python');
  theme = input<'vs-dark' | 'vs-light'>('vs-dark');
  height = input<string>('500px');
  readOnly = input<boolean>(false);

  // Output
  codeChange = output<string>();

  // Editor reference
  private editorContainer = viewChild<ElementRef<HTMLDivElement>>('editorContainer');
  private editor?: monaco.editor.IStandaloneCodeEditor;

  constructor() {
    // Initialize editor when container is ready
    effect(() => {
      const container = this.editorContainer();
      if (container) {
        this.initEditor(container.nativeElement);
      }
    });

    // Update editor when inputs change
    effect(() => {
      if (this.editor) {
        this.editor.setValue(this.code());
      }
    });

    effect(() => {
      if (this.editor) {
        monaco.editor.setModelLanguage(this.editor.getModel()!, this.language());
      }
    });

    effect(() => {
      if (this.editor) {
        monaco.editor.setTheme(this.theme());
      }
    });
  }

  private initEditor(container: HTMLDivElement) {
    this.editor = monaco.editor.create(container, {
      value: this.code(),
      language: this.language(),
      theme: this.theme(),
      automaticLayout: true,
      minimap: { enabled: false },
      fontSize: 14,
      lineNumbers: 'on',
      roundedSelection: true,
      scrollBeyondLastLine: false,
      readOnly: this.readOnly(),
      cursorStyle: 'line',
      wordWrap: 'on',
    });

    // Emit code changes
    this.editor.onDidChangeModelContent(() => {
      const newCode = this.editor!.getValue();
      this.codeChange.emit(newCode);
    });
  }

  ngOnDestroy() {
    this.editor?.dispose();
  }
}