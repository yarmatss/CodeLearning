import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PaginationComponent } from './pagination.component';

describe('PaginationComponent', () => {
  let fixture: ComponentFixture<PaginationComponent>;

  function render(page: number, totalPages: number): HTMLElement {
    fixture.componentRef.setInput('page', page);
    fixture.componentRef.setInput('totalPages', totalPages);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [PaginationComponent] });
    fixture = TestBed.createComponent(PaginationComponent);
  });

  it('is hidden when everything fits on one page', () => {
    expect(render(1, 1).querySelector('nav')).toBeNull();
  });

  it('emits the next and previous page and disables the ends', () => {
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe(p => emitted.push(p));

    const el = render(1, 3);
    const [prev, next] = Array.from(el.querySelectorAll('button'));
    expect(prev.disabled).toBe(true);
    next.click();

    const last = render(3, 3);
    const [prev2, next2] = Array.from(last.querySelectorAll('button'));
    expect(next2.disabled).toBe(true);
    prev2.click();

    expect(emitted).toEqual([2, 2]);
  });

  it('ignores out-of-range pages', () => {
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe(p => emitted.push(p));
    render(2, 3);
    fixture.componentInstance.go(0);
    fixture.componentInstance.go(4);
    fixture.componentInstance.go(2);
    expect(emitted).toEqual([]);
  });
});
