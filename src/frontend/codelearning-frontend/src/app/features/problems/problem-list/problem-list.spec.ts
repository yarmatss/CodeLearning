import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ProblemList } from './problem-list';

describe('ProblemList', () => {
  let component: ProblemList;
  let fixture: ComponentFixture<ProblemList>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProblemList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ProblemList);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
