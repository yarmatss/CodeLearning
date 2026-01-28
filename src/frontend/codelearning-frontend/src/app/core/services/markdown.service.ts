import { Injectable } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { marked, Renderer } from 'marked';
import DOMPurify from 'dompurify';
import hljs from 'highlight.js';

@Injectable({
  providedIn: 'root'
})
export class MarkdownService {
  constructor(private sanitizer: DomSanitizer) {
    // Configure marked with custom renderer for code blocks
    const renderer = new Renderer();
    
    renderer.code = ({ text, lang }: { text: string; lang?: string }) => {
      if (lang && hljs.getLanguage(lang)) {
        try {
          const highlighted = hljs.highlight(text, { language: lang }).value;
          return `<pre><code class="hljs language-${lang}">${highlighted}</code></pre>`;
        } catch (err) {
          console.error('Highlight error:', err);
        }
      }
      const highlighted = hljs.highlightAuto(text).value;
      return `<pre><code class="hljs">${highlighted}</code></pre>`;
    };

    marked.setOptions({
      breaks: true,
      gfm: true,
      renderer: renderer
    });
  }

  /**
   * Decodes HTML entities in a string
   */
  private decodeHtmlEntities(text: string): string {
    const textarea = document.createElement('textarea');
    textarea.innerHTML = text;
    return textarea.value;
  }

  /**
   * Converts markdown to sanitized HTML
   */
  async renderMarkdown(markdown: string): Promise<SafeHtml> {
    if (!markdown) {
      return '';
    }

    try {
      // Decode HTML entities that may have been encoded by backend
      const decodedMarkdown = this.decodeHtmlEntities(markdown);
      // Convert markdown to HTML
      const rawHtml = await marked(decodedMarkdown);
      
      // Sanitize HTML to prevent XSS with DOMPurify
      const cleanHtml = DOMPurify.sanitize(rawHtml as string, {
        ALLOWED_TAGS: [
          'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
          'p', 'br', 'hr', 'div', 'span',
          'strong', 'em', 'u', 's', 'code', 'pre',
          'ul', 'ol', 'li',
          'blockquote',
          'a',
          'table', 'thead', 'tbody', 'tr', 'th', 'td',
          'img'
        ],
        ALLOWED_ATTR: ['href', 'target', 'rel', 'src', 'alt', 'title', 'class', 'id']
      });

      // Bypass Angular's sanitizer since DOMPurify already cleaned the HTML
      return this.sanitizer.bypassSecurityTrustHtml(cleanHtml);
    } catch (error) {
      console.error('Error rendering markdown:', error);
      return '';
    }
  }

  /**
   * Synchronous version for simple cases
   */
  renderMarkdownSync(markdown: string): SafeHtml {
    if (!markdown) {
      return '';
    }

    try {
      // Decode HTML entities that may have been encoded by backend
      const decodedMarkdown = this.decodeHtmlEntities(markdown);
      const rawHtml = marked.parse(decodedMarkdown, { async: false }) as string;
      
      // Sanitize HTML to prevent XSS with DOMPurify
      const cleanHtml = DOMPurify.sanitize(rawHtml, {
        ALLOWED_TAGS: [
          'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
          'p', 'br', 'hr', 'div', 'span',
          'strong', 'em', 'u', 's', 'code', 'pre',
          'ul', 'ol', 'li',
          'blockquote',
          'a',
          'table', 'thead', 'tbody', 'tr', 'th', 'td',
          'img'
        ],
        ALLOWED_ATTR: ['href', 'target', 'rel', 'src', 'alt', 'title', 'class', 'id']
      });

      // Bypass Angular's sanitizer since DOMPurify already cleaned the HTML
      return this.sanitizer.bypassSecurityTrustHtml(cleanHtml);
    } catch (error) {
      console.error('Error rendering markdown:', error);
      return '';
    }
  }
}

