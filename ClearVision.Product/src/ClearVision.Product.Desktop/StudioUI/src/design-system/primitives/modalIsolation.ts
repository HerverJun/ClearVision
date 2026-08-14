interface ModalIsolationSnapshot {
  readonly backgroundRoot: HTMLElement | null;
  readonly backgroundWasInert: boolean;
  readonly backgroundAriaHidden: string | null;
  readonly bodyOverflow: string;
  readonly bodyPaddingRight: string;
}

let activeLeaseCount = 0;
let snapshot: ModalIsolationSnapshot | null = null;

export function acquireModalIsolation(): () => void {
  if (activeLeaseCount === 0) {
    const backgroundRoot = document.querySelector<HTMLElement>('#app');
    snapshot = {
      backgroundRoot,
      backgroundWasInert: Boolean(backgroundRoot?.inert),
      backgroundAriaHidden: backgroundRoot?.getAttribute('aria-hidden') ?? null,
      bodyOverflow: document.body.style.overflow,
      bodyPaddingRight: document.body.style.paddingRight
    };

    if (backgroundRoot) {
      backgroundRoot.inert = true;
      backgroundRoot.setAttribute('aria-hidden', 'true');
    }
    const scrollbarWidth = Math.max(0, window.innerWidth - document.documentElement.clientWidth);
    document.body.style.overflow = 'hidden';
    if (scrollbarWidth > 0) document.body.style.paddingRight = `${scrollbarWidth}px`;
  }

  activeLeaseCount += 1;
  let released = false;
  return () => {
    if (released) return;
    released = true;
    activeLeaseCount = Math.max(0, activeLeaseCount - 1);
    if (activeLeaseCount > 0 || !snapshot) return;

    const current = snapshot;
    snapshot = null;
    if (current.backgroundRoot) {
      current.backgroundRoot.inert = current.backgroundWasInert;
      if (current.backgroundAriaHidden === null) current.backgroundRoot.removeAttribute('aria-hidden');
      else current.backgroundRoot.setAttribute('aria-hidden', current.backgroundAriaHidden);
    }
    document.body.style.overflow = current.bodyOverflow;
    document.body.style.paddingRight = current.bodyPaddingRight;
  };
}
