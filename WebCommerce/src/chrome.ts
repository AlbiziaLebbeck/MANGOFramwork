import { useLayoutEffect, type RefObject } from 'react';

// The owned WebGL template and DOM panels share these measured, non-overlapping regions.
export function useCommerceChrome(header: RefObject<HTMLElement | null>, toast: RefObject<HTMLElement | null>, admin: boolean, hasToast: boolean) {
  useLayoutEffect(() => {
    const element = header.current;
    if (!element) return;
    const nav = element.querySelector('nav');
    const message = toast.current;
    const style = document.documentElement.style;
    const update = () => {
      const h = element.getBoundingClientRect();
      const n = nav?.getBoundingClientRect();
      const bottom = n && n.top >= h.bottom ? Math.max(0, window.innerHeight - n.top) : 0;
      style.setProperty('--mangos-commerce-top', `${Math.ceil(h.bottom)}px`);
      style.setProperty('--mangos-commerce-bottom', `${Math.ceil(bottom)}px`);
      style.setProperty('--mangos-commerce-toast', `${Math.ceil(message?.getBoundingClientRect().height ?? 0)}px`);
    };
    const observer = new ResizeObserver(update);
    observer.observe(element);
    if (nav) observer.observe(nav);
    if (message) observer.observe(message);
    window.addEventListener('resize', update);
    update();
    return () => {
      observer.disconnect();
      window.removeEventListener('resize', update);
      style.removeProperty('--mangos-commerce-top');
      style.removeProperty('--mangos-commerce-bottom');
      style.removeProperty('--mangos-commerce-toast');
    };
  }, [header, toast, admin, hasToast]);
}
