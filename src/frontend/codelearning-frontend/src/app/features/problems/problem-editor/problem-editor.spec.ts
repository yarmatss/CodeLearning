import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ProblemEditor } from './problem-editor';

describe('ProblemEditor', () => {
  let component: ProblemEditor;
  let fixture: ComponentFixture<ProblemEditor>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProblemEditor]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ProblemEditor);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
