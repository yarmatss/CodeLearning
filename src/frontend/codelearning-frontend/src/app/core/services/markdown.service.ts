import { Injectable } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { marked } from 'marked';
import DOMPurify from 'dompurify';

@Injectable({
  providedIn: 'root'
})
export class MarkdownService {
  constructor(private sanitizer: DomSanitizer) {
    // Configure marked options
    marked.setOptions({
      breaks: true,
      gfm: true
    });
  }

  /**
   * Converts markdown to sanitized HTML
   */
  async renderMarkdown(markdown: string): Promise<SafeHtml> {
    if (!markdown) {
      return '';
    }

    try {
      // Convert markdown to HTML
      const rawHtml = await marked(markdown);
      
      // Sanitize HTML to prevent XSS with DOMPurify
      const cleanHtml = DOMPurify.sanitize(rawHtml as string, {
        ALLOWED_TAGS: [
          'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
          'p', 'br', 'hr',
          'strong', 'em', 'u', 's', 'code', 'pre',
          'ul', 'ol', 'li',
          'blockquote',
          'a',
          'table', 'thead', 'tbody', 'tr', 'th', 'td',
          'img'
        ],
        ALLOWED_ATTR: ['href', 'target', 'rel', 'src', 'alt', 'title', 'class']
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
      const rawHtml = marked.parse(markdown, { async: false }) as string;
      
      // Sanitize HTML to prevent XSS with DOMPurify
      const cleanHtml = DOMPurify.sanitize(rawHtml, {
        ALLOWED_TAGS: [
          'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
          'p', 'br', 'hr',
          'strong', 'em', 'u', 's', 'code', 'pre',
          'ul', 'ol', 'li',
          'blockquote',
          'a',
          'table', 'thead', 'tbody', 'tr', 'th', 'td',
          'img'
        ],
        ALLOWED_ATTR: ['href', 'target', 'rel', 'src', 'alt', 'title', 'class']
      });

      // Bypass Angular's sanitizer since DOMPurify already cleaned the HTML
      return this.sanitizer.bypassSecurityTrustHtml(cleanHtml);
    } catch (error) {
      console.error('Error rendering markdown:', error);
      return '';
    }
  }
}

