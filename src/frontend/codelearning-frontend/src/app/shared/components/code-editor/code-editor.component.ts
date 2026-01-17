import {
  Component,
  input,
  output,
  effect,
  viewChild,
  ElementRef,
  ChangeDetectionStrategy,
  OnDestroy,
  signal
} from '@angular/core';
import * as monaco from 'monaco-editor';

@Component({
  selector: 'app-code-editor',
  template: `<div #editorContainer class="h-[600px] w-full"></div>`,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CodeEditorComponent implements OnDestroy {
  code = input.required<string>();
  language = input.required<'python' | 'javascript' | 'csharp' | 'java'>();
  readonly = input<boolean>(false);
  codeChange = output<string>();

  private editorContainer = viewChild<ElementRef>('editorContainer');
  private editor?: monaco.editor.IStandaloneCodeEditor;
  private isInitialized = signal(false);

  constructor() {
    effect(() => {
      const container = this.editorContainer();
      if (container && !this.isInitialized()) {
        this.initEditor();
      }
    });

    effect(() => {
      if (this.editor && this.isInitialized()) {
        const currentValue = this.editor.getValue();
        const newValue = this.code();
        if (currentValue !== newValue) {
          this.editor.setValue(newValue);
        }
      }
    });

    effect(() => {
      if (this.editor) {
        this.editor.updateOptions({ readOnly: this.readonly() });
      }
    });
  }

  private initEditor(): void {
    const container = this.editorContainer()!.nativeElement;
    
    this.editor = monaco.editor.create(container, {
      value: this.code(),
      language: this.language(),
      theme: 'vs-dark',
      automaticLayout: true,
      minimap: { enabled: false },
      fontSize: 14,
      readOnly: this.readonly(),
      scrollBeyondLastLine: false,
      wordWrap: 'on',
      lineNumbers: 'on',
      renderWhitespace: 'selection',
    });

    this.editor.onDidChangeModelContent(() => {
      if (this.editor && !this.readonly()) {
        this.codeChange.emit(this.editor.getValue());
      }
    });

    this.isInitialized.set(true);
  }

  ngOnDestroy(): void {
    this.editor?.dispose();
  }
}
