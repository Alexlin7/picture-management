import { shouldAutoCollapse, INSPECTOR_COLLAPSE, FACET_COLLAPSE } from './layout-breakpoints';

describe('layout breakpoints', () => {
  it('collapses below threshold', () => {
    expect(shouldAutoCollapse(INSPECTOR_COLLAPSE - 1, INSPECTOR_COLLAPSE)).toBe(true);
    expect(shouldAutoCollapse(FACET_COLLAPSE - 1, FACET_COLLAPSE)).toBe(true);
  });
  it('does not collapse at or above threshold', () => {
    expect(shouldAutoCollapse(INSPECTOR_COLLAPSE, INSPECTOR_COLLAPSE)).toBe(false);
    expect(shouldAutoCollapse(2000, INSPECTOR_COLLAPSE)).toBe(false);
  });
  it('treats non-positive width as not collapsed (unmeasured)', () => {
    expect(shouldAutoCollapse(0, INSPECTOR_COLLAPSE)).toBe(false);
  });
});
