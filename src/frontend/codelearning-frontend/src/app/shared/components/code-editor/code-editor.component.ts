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
import type * as Monaco from 'monaco-editor';
import { EditorLanguage, loadMonaco, MonacoApi } from './monaco-loader';

@Component({
  selector: 'app-code-editor',
  template: `
    <div #editorContainer class="h-[600px] w-full">
      @if (loadError()) {
        <p class="p-4 text-sm text-red-700">The code editor failed to load. Reload the page to try again.</p>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CodeEditorComponent implements OnDestroy {
  code = input.required<string>();
  language = input.required<EditorLanguage>();
  readonly = input<boolean>(false);
  codeChange = output<string>();

  protected readonly loadError = signal(false);

  private editorContainer = viewChild<ElementRef<HTMLElement>>('editorContainer');
  private monaco?: MonacoApi;
  private editor?: Monaco.editor.IStandaloneCodeEditor;
  private readonly isInitialized = signal(false);
  private initStarted = false;
  private destroyed = false;

  constructor() {
    effect(() => {
      const container = this.editorContainer();
      if (container && !this.initStarted) {
        this.initStarted = true;
        void this.initEditor(container.nativeElement);
      }
    });

    effect(() => {
      const newValue = this.code();
      if (this.isInitialized() && this.editor && this.editor.getValue() !== newValue) {
        this.editor.setValue(newValue);
      }
    });

    effect(() => {
      const readOnly = this.readonly();
      if (this.isInitialized()) {
        this.editor?.updateOptions({ readOnly });
      }
    });

    // Switching language keeps the editor (and its undo history) and only retokenizes the model
    effect(() => {
      const language = this.language();
      const model = this.isInitialized() ? this.editor?.getModel() : null;
      if (model && this.monaco && model.getLanguageId() !== language) {
        this.monaco.editor.setModelLanguage(model, language);
      }
    });
  }

  private async initEditor(container: HTMLElement): Promise<void> {
    try {
      this.monaco = await loadMonaco();
    } catch {
      this.loadError.set(true);
      return;
    }

    if (this.destroyed) {
      return;
    }

    this.editor = this.monaco.editor.create(container, {
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
    this.destroyed = true;
    this.editor?.dispose();
  }
}
