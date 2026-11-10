import { Injectable, inject } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Marked, Tokens } from 'marked';
import DOMPurify from 'dompurify';
import hljs from 'highlight.js/lib/core';
import bash from 'highlight.js/lib/languages/bash';
import c from 'highlight.js/lib/languages/c';
import cpp from 'highlight.js/lib/languages/cpp';
import csharp from 'highlight.js/lib/languages/csharp';
import css from 'highlight.js/lib/languages/css';
import java from 'highlight.js/lib/languages/java';
import javascript from 'highlight.js/lib/languages/javascript';
import json from 'highlight.js/lib/languages/json';
import plaintext from 'highlight.js/lib/languages/plaintext';
import python from 'highlight.js/lib/languages/python';
import sql from 'highlight.js/lib/languages/sql';
import typescript from 'highlight.js/lib/languages/typescript';
import xml from 'highlight.js/lib/languages/xml';

// Only the languages the courses teach (plus common snippet formats) are bundled, instead of
// every grammar highlight.js ships with
const HIGHLIGHT_LANGUAGES = { bash, c, cpp, csharp, css, java, javascript, json, plaintext, python, sql, typescript, xml };
for (const [name, language] of Object.entries(HIGHLIGHT_LANGUAGES)) {
  if (!hljs.getLanguage(name)) {
    hljs.registerLanguage(name, language);
  }
}

const ALLOWED_TAGS = [
  'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
  'p', 'br', 'hr', 'div', 'span',
  'strong', 'em', 'u', 's', 'del', 'code', 'pre',
  'ul', 'ol', 'li',
  'blockquote',
  'a',
  'table', 'thead', 'tbody', 'tr', 'th', 'td',
  'img'
];

// No `class`, `id`, `style`, `target` or `rel`: authored content cannot borrow the app's CSS
// classes, clobber element ids or choose how links open. Highlighting classes and link targets
// are put back by the hooks below.
const ALLOWED_ATTR = ['href', 'src', 'alt', 'title'];

// Classes highlight.js itself emits: `hljs`, `hljs-keyword`, ... and the `language-x` marker
const HIGHLIGHT_CLASS = /^(?:hljs(?:-[a-z0-9_-]+)?|language-[a-z0-9_+#-]+)$/i;

/** Renders authored markdown (theory, problem and course descriptions) as sanitized HTML. */
@Injectable({
  providedIn: 'root'
})
export class MarkdownService {
  private readonly sanitizer = inject(DomSanitizer);

  // A private instance, so the options and renderer set here do not leak into other users of
  // the global `marked` object
  private readonly marked = new Marked({
    breaks: true,
    gfm: true,
    async: false,
    renderer: {
      code({ text, lang }: Tokens.Code): string {
        const language = (lang ?? '').trim().split(/\s+/)[0].toLowerCase();
        if (language && hljs.getLanguage(language)) {
          const highlighted = hljs.highlight(text, { language, ignoreIllegals: true }).value;
          return `<pre><code class="hljs language-${language}">${highlighted}</code></pre>`;
        }
        const highlighted = hljs.highlightAuto(text).value;
        return `<pre><code class="hljs">${highlighted}</code></pre>`;
      }
    }
  });

  // A private DOMPurify instance, so these hooks apply only to markdown rendered here
  private readonly purify = createPurifier();

  /** Converts markdown to sanitized HTML that is safe to bind to `[innerHTML]`. */
  render(markdown: string | null | undefined): SafeHtml {
    return this.sanitizer.bypassSecurityTrustHtml(this.toSafeHtmlString(markdown));
  }

  /** The sanitized HTML as a string (exposed for tests and non-template use). */
  toSafeHtmlString(markdown: string | null | undefined): string {
    if (!markdown) {
      return '';
    }

    try {
      // The backend stores markdown as written; DOMPurify below is the sanitization boundary
      const rawHtml = this.marked.parse(markdown, { async: false }) as string;
      return this.purify.sanitize(rawHtml, {
        ALLOWED_TAGS,
        ALLOWED_ATTR,
        ALLOW_DATA_ATTR: false
      }) as string;
    } catch {
      return '';
    }
  }
}

function createPurifier(): ReturnType<typeof DOMPurify> {
  const purify = DOMPurify(window);

  // `class` is not allowed in general; keep only highlight.js classes on code and spans
  purify.addHook('uponSanitizeAttribute', (node, data) => {
    if (data.attrName !== 'class') {
      return;
    }

    const tag = node.nodeName.toLowerCase();
    const kept = tag === 'span' || tag === 'code'
      ? data.attrValue.split(/\s+/).filter(token => HIGHLIGHT_CLASS.test(token))
      : [];

    if (kept.length === 0) {
      data.keepAttr = false;
      return;
    }

    // A forced attribute keeps the value already on the node, so the filtered one is written first
    (node as Element).setAttribute('class', kept.join(' '));
    data.forceKeepAttr = true;
  });

  // Every link opens in a new tab without giving the target page a handle to this one
  purify.addHook('afterSanitizeAttributes', (node) => {
    if (node.nodeName.toLowerCase() === 'a' && (node as Element).hasAttribute('href')) {
      (node as Element).setAttribute('target', '_blank');
      (node as Element).setAttribute('rel', 'noopener noreferrer');
    }
  });

  return purify;
}
