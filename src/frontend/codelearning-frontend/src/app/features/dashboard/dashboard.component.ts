import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-dashboard',
  imports: [],
  template: `
    <div class="mx-auto max-w-7xl px-4 py-8">
      <h1 class="mb-6 text-3xl font-bold text-gray-900">Dashboard</h1>
      <div class="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
        <p class="text-gray-700">Welcome to CodeLearning! Your dashboard will appear here.</p>
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardComponent {}
