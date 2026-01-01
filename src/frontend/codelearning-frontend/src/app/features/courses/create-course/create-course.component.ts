import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CourseService } from '../../../core/services/course.service';
import { CreateCourseRequest } from '../../../core/models/course.model';

@Component({
  selector: 'app-create-course',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './create-course.component.html',
  styleUrl: './create-course.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CreateCourseComponent {
  private readonly fb = inject(FormBuilder);
  private readonly courseService = inject(CourseService);
  private readonly router = inject(Router);

  protected readonly courseForm: FormGroup;
  protected readonly errorMessage = signal<string>('');
  protected readonly isLoading = signal(false);

  constructor() {
    this.courseForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(3)]],
      description: ['', [Validators.required, Validators.minLength(10)]]
    });
  }

  onSubmit(): void {
    if (this.courseForm.invalid) {
      this.courseForm.markAllAsTouched();
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    this.courseService.createCourse(this.courseForm.value as CreateCourseRequest).subscribe({
      next: (course) => {
        this.isLoading.set(false);
        this.router.navigate(['/courses', course.id, 'edit']);
      },
      error: (error: any) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to create course');
      }
    });
  }
}
