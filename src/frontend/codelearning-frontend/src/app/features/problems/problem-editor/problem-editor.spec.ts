import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ProblemEditor } from './problem-editor';

describe('ProblemEditor', () => {
  let component: ProblemEditor;
  let fixture: ComponentFixture<ProblemEditor>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProblemEditor],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
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
