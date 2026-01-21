import { Component } from '@angular/core';
import { SkeletonComponent } from '../skeleton/skeleton.component';

@Component({
  selector: 'app-course-skeleton',
  standalone: true,
  imports: [SkeletonComponent],
  template: `
    <div class="rounded-lg border border-gray-200 bg-white p-6">
      <app-skeleton height="1.5rem" customClass="mb-4" />
      <app-skeleton height="4rem" customClass="mb-4" />
      <div class="flex gap-4">
        <app-skeleton width="5rem" height="1.5rem" />
        <app-skeleton width="5rem" height="1.5rem" />
      </div>
    </div>
  `
})
export class CourseSkeletonComponent {}
