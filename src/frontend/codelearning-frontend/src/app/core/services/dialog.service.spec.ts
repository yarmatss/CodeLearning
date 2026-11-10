import { DialogService } from './dialog.service';

describe('DialogService', () => {
  it('queues dialogs and resolves each with its own answer', async () => {
    const service = new DialogService();
    const first = service.confirm({ title: 'First', message: '1' });
    const second = service.confirm({ title: 'Second', message: '2' });

    expect(service.config()?.title).toBe('First');
    service.close(true);
    expect(await first).toEqual({ confirmed: true });

    expect(service.isOpen()).toBe(true);
    expect(service.config()?.title).toBe('Second');
    service.close(false);
    expect(await second).toEqual({ confirmed: false });
    expect(service.isOpen()).toBe(false);
  });

  it('reset cancels the open and queued dialogs', async () => {
    const service = new DialogService();
    const first = service.confirm({ title: 'First', message: '1' });
    const second = service.confirm({ title: 'Second', message: '2' });

    service.reset();

    expect(await first).toEqual({ confirmed: false });
    expect(await second).toEqual({ confirmed: false });
    expect(service.isOpen()).toBe(false);
  });
});
