# Markdown Rendering

**Install:**
npm install marked @types/marked dompurify @types/dompurify

**Pipe:**
import { Pipe, PipeTransform } from '@angular/core'; import { marked } from 'marked'; import DOMPurify from 'dompurify';
@Pipe({ name: 'markdown' }) export class MarkdownPipe implements PipeTransform { transform(value: string): string { const html = marked.parse(value); return DOMPurify.sanitize(html as string); } }

**Usage:**
<div [innerHTML]="theoryContent() | markdown" class="prose max-w-none"></div>

**Tailwind Typography (optional):**
npm install -D @tailwindcss/typography
// tailwind.config.js module.exports = { plugins: [ require('@tailwindcss/typography'), ], }
