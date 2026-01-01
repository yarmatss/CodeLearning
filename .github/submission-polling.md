# Submission Polling Strategy

**Until SignalR is implemented, use polling:**
@Component({...}) export class SubmissionResultComponent { submissionId = input.required<string>(); submission = signal<Submission | null>(null); isPolling = signal(false);
constructor() { const submissionService = inject(SubmissionService);
effect(() => {
  const id = this.submissionId();
  if (id) {
    this.pollSubmission(id);
  }
});
}
private async pollSubmission(id: string) { this.isPolling.set(true); const maxAttempts = 60; // 2 minutes (2s interval)
for (let i = 0; i < maxAttempts; i++) {
  try {
    const sub = await firstValueFrom(
      this.submissionService.getById(id)
    );
    
    this.submission.set(sub);
    
    const finalStatuses = [
      'Completed',
      'CompilationError',
      'RuntimeError',
      'TimeLimitExceeded'
    ];
    
    if (finalStatuses.includes(sub.status)) {
      this.isPolling.set(false);
      break;
    }
    
    await new Promise(resolve => setTimeout(resolve, 2000));
  } catch (error) {
    console.error('Polling error:', error);
    this.isPolling.set(false);
    break;
  }
}

this.isPolling.set(false);
} }

**Template:**
@if (isPolling()) { <div class="flex items-center gap-2"> <div class="h-5 w-5 animate-spin rounded-full border-2 border-blue-600 border-t-transparent"></div> <span>Executing code...</span> </div> }
@if (submission(); as sub) { <div [class]="getStatusClass(sub.status)"> Status: {{ sub.status }} @if (sub.score !== undefined) { <span>Score: {{ sub.score }}%</span> } </div> }
