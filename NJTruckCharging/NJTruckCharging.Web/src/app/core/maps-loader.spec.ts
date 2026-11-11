import { mapsAuthFailed, onMapsAuthFailure } from './maps-loader';

// Module state is shared, so this file runs the whole key-rejection lifecycle in order.
describe('maps-loader key rejection (gm_authFailure)', () => {
  it('notifies current listeners, skips unsubscribed ones, and tells late subscribers at once', () => {
    const calls: string[] = [];
    onMapsAuthFailure(() => calls.push('a'));
    const stop = onMapsAuthFailure(() => calls.push('b'));
    stop();
    expect(calls).toEqual([]);

    mapsAuthFailed();
    expect(calls).toEqual(['a']);

    onMapsAuthFailure(() => calls.push('late'));
    expect(calls).toEqual(['a', 'late']);
  });
});
