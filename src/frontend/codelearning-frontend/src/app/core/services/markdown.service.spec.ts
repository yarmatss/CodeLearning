import { TestBed } from '@angular/core/testing';
import { MarkdownService } from './markdown.service';

describe('MarkdownService', () => {
  let service: MarkdownService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(MarkdownService);
  });

  it('drops author-supplied class and id attributes', () => {
    const html = service.toSafeHtmlString('<p class="fixed inset-0" id="navbar">hi</p>');
    expect(html).toContain('<p>hi</p>');
    expect(html).not.toContain('class=');
    expect(html).not.toContain('id=');
  });

  it('keeps highlight.js classes on code blocks', () => {
    const html = service.toSafeHtmlString('```python\nprint("x")\n```');
    expect(html).toContain('class="hljs language-python"');
    expect(html).toMatch(/class="hljs-[a-z]+"/);
  });

  it('forces links to open safely in a new tab', () => {
    const html = service.toSafeHtmlString('<a href="https://example.com" target="_self" rel="opener">x</a>');
    expect(html).toContain('target="_blank"');
    expect(html).toContain('rel="noopener noreferrer"');
    expect(html).not.toContain('_self');
  });

  it('removes script and event handlers', () => {
    const html = service.toSafeHtmlString('<img src="x" onerror="alert(1)"><script>alert(2)</script>');
    expect(html).not.toContain('onerror');
    expect(html).not.toContain('<script');
  });
});
