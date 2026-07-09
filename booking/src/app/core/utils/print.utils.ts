export function printWhenImagesReady(
  containerSelector: string,
  onAfterPrint?: () => void,
  fallbackMs = 2000
): void {
  requestAnimationFrame(() => {
    const container = document.querySelector(containerSelector);
    if (!container) {
      const previousTitle = document.title;
      document.title = ' ';
      window.print();
      document.title = previousTitle;
      onAfterPrint?.();
      return;
    }

    const images = Array.from(container.querySelectorAll('img'));
    const pending = images.filter(img => !img.complete);

    let printed = false;
    const runPrint = () => {
      if (printed) return;
      printed = true;
      const previousTitle = document.title;
      document.title = ' ';
      
      const appRoot = document.querySelector('app-root') as HTMLElement;
      const originalParent = container.parentElement;
      const originalNextSibling = container.nextSibling;
      
      if (appRoot && originalParent) {
        document.body.appendChild(container);
        appRoot.style.display = 'none';
      }

      window.print();

      if (appRoot && originalParent) {
        if (originalNextSibling) {
          originalParent.insertBefore(container, originalNextSibling);
        } else {
          originalParent.appendChild(container);
        }
        appRoot.style.display = '';
      }

      document.title = previousTitle;
      onAfterPrint?.();
    };

    if (pending.length === 0) {
      runPrint();
      return;
    }

    let loaded = 0;
    const onDone = () => {
      loaded++;
      if (loaded >= pending.length) {
        runPrint();
      }
    };

    pending.forEach(img => {
      img.addEventListener('load', onDone, { once: true });
      img.addEventListener('error', onDone, { once: true });
    });

    setTimeout(runPrint, fallbackMs);
  });
}
