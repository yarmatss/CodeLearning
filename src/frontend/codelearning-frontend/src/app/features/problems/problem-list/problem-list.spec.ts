import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ProblemList } from './problem-list';

describe('ProblemList', () => {
  let component: ProblemList;
  let fixture: ComponentFixture<ProblemList>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProblemList]
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
