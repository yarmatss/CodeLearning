import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-skeleton',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div 
      [class]="'animate-pulse rounded bg-gray-200 ' + customClass()"
      [style.width]="width()"
      [style.height]="height()"
    ></div>
  `,
  styles: [`
    @keyframes pulse {
      0%, 100% {
        opacity: 1;
      }
      50% {
        opacity: 0.5;
      }
    }
    .animate-pulse {
      animation: pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
    }
  `]
})
export class SkeletonComponent {
  width = input<string>('100%');
  height = input<string>('1rem');
  customClass = input<string>('');
}
